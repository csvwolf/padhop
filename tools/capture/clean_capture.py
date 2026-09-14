"""Conservative offline action validation. Originals are never overwritten."""
import bisect, csv, json, math, pathlib, statistics, sys

def clean(records, inputs):
    rows=sorted(inputs,key=lambda x:float(x['unix_seconds']))
    times=[float(x['unix_seconds']) for x in rows]
    def num(r,k):return float(r.get(k,0) or 0)
    def distance(a,b):return math.hypot(num(a,'x')-num(b,'x'),num(a,'y')-num(b,'y'))
    noise=[distance(a,b) for a,b in zip(rows,rows[1:]) if a.get('phase')==b.get('phase')=='idle' and num(a,'touch') and num(b,'touch')]
    median=statistics.median(noise) if noise else 20
    mad=statistics.median(abs(x-median) for x in noise) if noise else 10
    floor=max(40,min(160,median+3*mad))
    kept=[]; rejected={}
    for r in records:
        t=float(r['unix_seconds']);j=bisect.bisect_left(times,t)
        indices=[k for k in (j-1,j) if 0<=k<len(rows)]
        reason=None; event=None
        if not indices:reason='missing_input'
        else:
            k=min(indices,key=lambda k:abs(times[k]-t));cur=rows[k];phase=cur.get('phase')
            nearby=rows[bisect.bisect_left(times,t-.08):bisect.bisect_right(times,t+.04)]
            if abs(times[k]-t)>.025:reason='timing_uncertain'
            elif phase not in ('slow_slide','fast_slide','press_release'):reason='idle_or_prepare'
            elif not nearby or any(x.get('phase')!=phase for x in nearby):reason='phase_boundary'
            elif phase in ('slow_slide','fast_slide'):
                history=rows[bisect.bisect_left(times,t-.06):k+1]
                if not num(cur,'touch') or len(history)<3:reason='no_continuous_touch'
                elif any(num(x,'pressure')>=1000 or num(x,'click_bit') for x in nearby):reason='slide_near_press'
                elif not all(num(x,'touch') for x in history):reason='touch_transition'
                elif distance(history[0],history[-1])<max(160,floor*4):reason='stationary_jitter'
            else:
                start=bisect.bisect_left(times,t-.06);end=bisect.bisect_right(times,t+.025)
                edges=[]
                for i in range(max(1,start),end):
                    before,after=rows[i-1],rows[i]
                    if before.get('phase')!='press_release' or after.get('phase')!='press_release':continue
                    rising=(num(after,'pressure')>=2500 and num(before,'pressure')<2500) or (num(after,'click_bit') and not num(before,'click_bit'))
                    falling=(num(after,'pressure')<1500 and num(before,'pressure')>=1500) or (not num(after,'click_bit') and num(before,'click_bit'))
                    if rising:edges.append((abs(times[i]-t),'press'))
                    if falling:edges.append((abs(times[i]-t),'release'))
                kinds={kind for _,kind in edges}
                event=min(edges)[1] if len(kinds)==1 else None
                around=rows[bisect.bisect_left(times,t-.035):bisect.bisect_right(times,t+.025)]
                movement=sum(distance(a,b) for a,b in zip(around,around[1:]) if num(a,'touch') and num(b,'touch'))
                if not edges:reason='not_click_edge'
                elif event is None:reason='ambiguous_click_edges'
                elif movement>max(500,floor*8):reason='press_with_sliding'
        if reason:rejected[reason]=rejected.get(reason,0)+1
        else:
            item=dict(r);item['nearest_input']=dict(cur)
            if event:item['nearest_input']['phase']=event
            kept.append(item)
    return kept,dict(version=2,total=len(records),kept=len(kept),rejected=rejected,noise_floor_raw=floor)

def existing(file):
    from auto_capture import candidates
    p=pathlib.Path(file);data=json.loads(p.read_text(encoding='utf-8-sig'))
    with (p.parent/'left-input.csv').open(encoding='utf-8-sig',newline='') as f:inputs=list(csv.DictReader(f))
    records=data.get('left_haptic_reports',[])
    if not records:raise ValueError('No original reports for action validation')
    good,stats=clean(records,inputs);data['candidates']=[x for x in candidates(good) if x['count']>=3];data['cleaning']=stats
    data['message']='已按触摸、位移和按压边沿筛选；没有足够干净样本时不生成配置。'
    out=p.parent/'haptics.cleaned.json';out.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8');return stats

def test():
    rows=[dict(unix_seconds=str(i*.005),phase='fast_slide',touch='1',click_bit='0',pressure='0',x=str(i*100),y='0') for i in range(100)]
    r=dict(unix_seconds=.25)
    assert len(clean([r],rows)[0])==1
    assert not clean([r],[dict(x,phase='idle') for x in rows])[0]
    assert not clean([r],[dict(x,x='0') for x in rows])[0]
    assert not clean([r],[dict(x,pressure='3000') for x in rows])[0]
    pressed=[dict(x,phase='press_release',x='0',pressure='3000' if i>=50 else '0') for i,x in enumerate(rows)]
    assert len(clean([r],pressed)[0])==1
    assert not clean([dict(unix_seconds=.35)],pressed)[0]
    moving=[dict(x,x=str(i*100)) for i,x in enumerate(pressed)]
    assert not clean([r],moving)[0]
    assert clean([r],pressed)[0][0]['nearest_input']['phase']=='press'
    released=[dict(x,pressure='3000' if i<50 else '0') for i,x in enumerate(pressed)]
    assert clean([r],released)[0][0]['nearest_input']['phase']=='release'
    bounce=[dict(x,click_bit='1' if i==50 else '0',pressure='0') for i,x in enumerate(pressed)]
    assert not clean([r],bounce)[0]
    print('PASS: slide/noise/pressure gates; separate press/release; reject hold, sliding clicks and ambiguous edges')
if __name__=='__main__':
    if sys.argv[1]=='--self-test':test()
    else:print(json.dumps(existing(sys.argv[1]),ensure_ascii=False))
