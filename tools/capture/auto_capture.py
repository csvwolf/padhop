"""Offline HCI/ATT discovery and candidate extraction. Never sends to hardware."""
import csv
import io
import json
import pathlib
import re
import statistics
import struct
import subprocess
import sys
from collections import defaultdict
from analyze import analyze


def pcap_packets(stream):
    header = stream.read(24)
    formats = {b'\xd4\xc3\xb2\xa1':('<',1e6), b'\xa1\xb2\xc3\xd4':('>',1e6),
               b'\x4d\x3c\xb2\xa1':('<',1e9), b'\xa1\xb2\x3c\x4d':('>',1e9)}
    if len(header)!=24 or header[:4] not in formats:
        raise ValueError('Unsupported capture format; expected classic HCI pcap from BTETLParse.')
    endian, scale = formats[header[:4]]
    link = struct.unpack_from(endian+'I',header,20)[0]
    if link not in (187,201):
        raise ValueError('Unsupported HCI link type: '+str(link))
    frame = 0
    while True:
        h = stream.read(16)
        if not h: return
        if len(h)!=16: raise ValueError('Truncated pcap record')
        sec,part,size,original = struct.unpack(endian+'IIII',h)
        if size>1048576 or size>original: raise ValueError('Invalid packet length')
        data=stream.read(size)
        if len(data)!=size or size!=original: raise ValueError('Truncated HCI packet')
        frame+=1
        direction=None
        if link==201:
            if len(data)<5: raise ValueError('Truncated pseudo-header')
            direction=struct.unpack('>I',data[:4])[0];data=data[4:]
        yield frame,sec+part/scale,direction,data


class Connection:
    def __init__(self,handle):
        self.handle=handle;self.chars={};self.descriptors={};self.refs={};self.pending={};self.rows=[]
    def att(self,frame,time,direction,p):
        if not p:return
        op=p[0]
        if op==0x08 and len(p)==7:
            self.pending['type']=struct.unpack_from('<H',p,5)[0]
        elif op==0x09 and len(p)>=2:
            size=p[1]
            if size<4 or (len(p)-2)%size:return
            for off in range(2,len(p),size):
                entry=p[off:off+size];h=struct.unpack_from('<H',entry)[0]
                if self.pending.get('type')==0x2803 and size==7:
                    value,uuid=struct.unpack_from('<HH',entry,3);self.chars[h]=(value,uuid)
                elif self.pending.get('type')==0x2908 and size==4:
                    self.refs[h]=(entry[2],entry[3]);self.descriptors[h]=0x2908
            self.pending.pop('type',None)
        elif op==0x05 and len(p)>=2 and p[1]==1 and (len(p)-2)%4==0:
            for off in range(2,len(p),4):
                h,uuid=struct.unpack_from('<HH',p,off);self.descriptors[h]=uuid
        elif op==0x0a and len(p)==3:self.pending['read']=struct.unpack_from('<H',p,1)[0]
        elif op==0x0b:
            h=self.pending.pop('read',None)
            if len(p)==3 and self.descriptors.get(h)==0x2908:self.refs[h]=(p[1],p[2])
        elif op==0x01 and len(p)>=2:
            if p[1]==0x0a:self.pending.pop('read',None)
            if p[1]==0x08:self.pending.pop('type',None)
        elif op in (0x12,0x52) and len(p)>=3:
            h=struct.unpack_from('<H',p,1)[0]
            self.rows.append({'frame.number':str(frame),'frame.time_epoch':str(time),
                'bthci_acl.handle':hex(self.handle),'btatt.opcode':hex(op),'btatt.handle':hex(h),'btatt.value':p[3:].hex()})
    def output_map(self):
        result={};declarations=sorted(self.chars)
        for descriptor,(report,kind) in self.refs.items():
            if kind!=2 or report not in (0x81,0x82,0x83,0x84,0x85):continue
            prior=[h for h in declarations if self.chars[h][0]<descriptor]
            if not prior:continue
            declaration=max(prior);value,uuid=self.chars[declaration]
            later=[h for h in declarations if h>declaration]
            if uuid==0x2a4d and (not later or descriptor<min(later)):result[hex(value)]=hex(report)
        return result


def discover(packets,mac,allow_unidentified=False):
    active={};all_connections=[];fragments={};notes=[]
    for frame,time,direction,p in packets:
        if not p:continue
        if p[0]==4 and len(p)>=3:
            if len(p)!=3+p[2]:continue
            if p[1]==0x3e and len(p)>=15 and p[3] in (1,10) and p[4]==0:
                handle=struct.unpack_from('<H',p,5)[0]&0xfff
                # Every successful connection starts a new epoch, even handle reuse.
                active.pop(handle,None)
                for key in list(fragments):
                    if key[0]==handle:fragments.pop(key)
                peer=p[9:15][::-1].hex()
                if peer==mac:
                    c=Connection(handle);active[handle]=c;all_connections.append(c)
            elif p[1]==5 and len(p)>=7:
                handle=struct.unpack_from('<H',p,4)[0]&0xfff;active.pop(handle,None)
                for key in list(fragments):
                    if key[0]==handle:fragments.pop(key)
            continue
        if p[0]!=2 or len(p)<5:continue
        flags,length=struct.unpack_from('<HH',p,1);handle=flags&0xfff;pb=(flags>>12)&3
        if length!=len(p)-5:continue
        if handle not in active and allow_unidentified:
            c=Connection(handle);active[handle]=c;all_connections.append(c)
        if handle not in active:continue
        data=p[5:];key=(handle,direction)
        if pb==1:
            if key not in fragments:continue
            fragments[key]+=data;data=fragments[key]
        else:
            fragments.pop(key,None)
        if len(data)<4:continue
        wanted,cid=struct.unpack_from('<HH',data)
        if len(data)<wanted+4:
            if direction is None:notes.append('Fragmented HCI without direction was skipped');continue
            fragments[key]=data;continue
        fragments.pop(key,None)
        if len(data)!=wanted+4:continue
        if cid==4:active[handle].att(frame,time,direction,data[4:])
    return all_connections,notes


def calibrated_maps(connections,markers):
    """Three distinct successful known writes must identify one unique epoch/channel.
    These short right-side writes are tied to the already-opened physical device.
    Do not infer a channel from packet shape alone or carry mappings across reconnects.
    """
    result=[]
    for report in (0x81,0x82,0x83):
        expected=[m for m in markers if m.get('success') is True and m.get('report_id')==report]
        if len(expected)!=3 or len({m['payload_hex'] for m in expected})!=3:continue
        matches=[]
        for c in connections:
            common=None
            for m in expected:
                wanted=bytes.fromhex(m['payload_hex']);channels=set()
                for row in c.rows:
                    t=float(row['frame.time_epoch'])
                    if not float(m['before'])-.1<=t<=float(m['after'])+.2:continue
                    data=bytes.fromhex(row['btatt.value'])
                    if data[:len(wanted)]==wanted and not any(data[len(wanted):]):channels.add(row['btatt.handle'])
                common=channels if common is None else common & channels
            for channel in common or () :matches.append((c,channel))
        if len(matches)==1:
            c,channel=matches[0]
            existing=next((m for conn,m in result if conn is c),None)
            if existing is None:existing={};result.append((c,existing))
            if channel in existing and existing[channel]!=hex(report):raise ValueError('Calibration report IDs collide on one channel')
            existing[channel]=hex(report)
    return result


def capture_diagnostics(packets):
    acl=0;trimmed=0;writes=0;empty_writes=0;connections=0
    for _,_,_,p in packets:
        if p and p[0]==4 and len(p)>3 and p[1]==0x3e and p[3] in (1,10):connections+=1
        if p and p[0]==2 and len(p)>=5:
            acl+=1
            if struct.unpack_from('<H',p,3)[0]!=len(p)-5:trimmed+=1
            if len(p)>=12 and p[9] in (0x12,0x52):
                writes+=1
                if len(p)==12:empty_writes+=1
    return dict(acl_packets=acl,trimmed_acl=trimmed,att_writes=writes,empty_writes=empty_writes,connection_events=connections)


def candidates(records):
    groups=defaultdict(list)
    for r in records:
        # Keep exact waveforms separate; never average different commands into a fictitious tone.
        phase=r.get('nearest_input',{}).get('phase','unclassified')
        groups[(phase,r['report_id'],r['payload_hex'])].append(r)
    result=[]
    for (phase,report,payload),rows in sorted(groups.items(),key=lambda item:-len(item[1])):
        item=rows[0].copy();item.pop('nearest_input',None)
        item['phase']=phase;item['count']=len(rows)
        intervals=[r['interval_ms_same_side'] for r in rows if 0<r.get('interval_ms_same_side',0)<=200]
        if intervals:item['suggested_interval_ms']=round(statistics.median(intervals))
        result.append(item)
    return result[:100]


def run(folder, parser=None):
    folder=pathlib.Path(folder).resolve();root=pathlib.Path(__file__).parent
    target=json.loads((folder/'target.json').read_text(encoding='utf-8-sig'))
    mac=target.get('bluetooth_mac','').lower()
    if not re.fullmatch('[0-9a-f]{12}',mac):raise ValueError('SC2 Bluetooth identity was not recorded; no device guess will be made.')
    pcap=folder/'bluetooth.pcap'
    subprocess.run([str(parser or root/'tools'/'BTETLParse.exe'),'-pcap',str(pcap),str(folder/'bluetooth.etl')],check=True,capture_output=True)
    if not pcap.exists():raise ValueError('ETL conversion produced no HCI file')
    with pcap.open('rb') as f:packets=list(pcap_packets(f))
    diagnostics=capture_diagnostics(packets)
    connections,notes=discover(packets,mac)
    with (folder/'left-input.csv').open(encoding='utf-8-sig',newline='') as f:inputs=list(csv.DictReader(f))
    records=[];maps=[];rejected=[]
    # Prefer experimentally verified writes when capture starts mid-connection.
    marker_path=folder/'calibration.json'
    marked=[]
    if marker_path.exists():
        epochs,_=discover(packets,mac,allow_unidentified=True)
        marked=calibrated_maps(epochs,json.loads(marker_path.read_text(encoding='utf-8-sig')))
    for c,mapping in marked:
        config=dict(verified_sc2=True,connection_handle=hex(c.handle),output_attributes=mapping,capture_side=target.get("capture_side","left"))
        result=analyze(c.rows,config,inputs);records+=result['left_haptic_reports'];rejected+=result['rejected'];maps.append(config)
    for c in connections:
        mapping=c.output_map()
        if not mapping:continue
        config=dict(verified_sc2=True,connection_handle=hex(c.handle),output_attributes=mapping,capture_side=target.get("capture_side","left"))
        result=analyze(c.rows,config,inputs);records+=result['left_haptic_reports'];rejected+=result['rejected'];maps.append(config)
    records=list({r['frame']:r for r in records}.values())
    failure='已确认 '+str(len({value for m in maps for value in m["output_attributes"].values()}))+' 类震动通道，完整内容已录到。若这是 8 秒检查，通路已打通；接下来才需要做完整左板采集。尚未生成可应用的左板候选。'
    if diagnostics['trimmed_acl'] and diagnostics['empty_writes']:
        failure='日志中的 HID 内容仍被系统裁掉：'+str(diagnostics['empty_writes'])+' 次写入只有头部，没有震动参数。不是操作问题，也不是没有震动；请保留日志检查完整记录开关。'
    elif not maps:
        failure='没有匹配到本次右板核对指令，无法确认震动通道。请保留日志和 calibration.json；无需先重连手柄。'
    result=dict(capture_side=target.get('capture_side','left'),status='decoded' if records else 'inconclusive',candidates=candidates(records),
        left_haptic_reports=records,maps=maps,rejected=rejected,notes=notes,diagnostics=diagnostics,
        message='已解析所选触摸板输出；时间对应仅供判断，不能证明发送进程。' if records else failure)
    (folder/'haptics.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    return result


def self_test():
    c=Connection(64)
    c.att(1,1,0,bytes.fromhex('080100ffff0328'))
    c.att(2,1,1,bytes.fromhex('090730000431004d2a'))
    c.att(3,1,1,bytes.fromhex('050132000829'))
    c.att(4,1,0,bytes.fromhex('0a3200'))
    c.att(5,1,1,bytes.fromhex('0b8202'))
    assert c.output_map()=={'0x31':'0x82'}
    c.att(6,1.1,0,bytes.fromhex('5231000101f1'))
    assert c.rows[0]['btatt.value']=='0101f1'
    # Exact address, connection epoch, and disconnect prevent handle-reuse contamination.
    event=bytes.fromhex('043e1301004000000001000000000200000000000000')
    peers,notes=discover([(1,1,0,event)],'020000000001');assert len(peers)==1
    peers,notes=discover([(1,1,0,event)],'000000000000');assert not peers
    header=struct.pack('<IHHIIII',0xa1b2c3d4,2,4,0,0,65535,201)
    packet=b'\0\0\0\1'+event
    blob=header+struct.pack('<IIII',1,2000,len(packet),len(packet))+packet
    parsed=list(pcap_packets(io.BytesIO(blob)));assert parsed[0][3]==event
    assert candidates([])==[]
    def acl(payload):
        l2=struct.pack('<HH',len(payload),4)+payload
        return b'\x02'+struct.pack('<HH',0x2040,len(l2))+l2
    packets=[(1,1,0,event)]
    for i,h in enumerate(['080100ffff0328','090730000431004d2a','050132000829','0a3200','0b8202','5231000001f1']):
        packets.append((i+2,1.01+i*.01,i%2,acl(bytes.fromhex(h))))
    connections,_=discover(packets,'020000000001');assert len(connections)==1
    c=connections[0];result=analyze(c.rows,dict(verified_sc2=True,connection_handle='0x40',output_attributes=c.output_map()),[])
    assert len(result['left_haptic_reports'])==1
    assert result['left_haptic_reports'][0]['gain_db']==-15
    # An unrelated peer reusing the same connection handle must not inherit the SC2 identity/map.
    other=event[:9]+bytes.fromhex('000000000000')+event[15:]
    packets += [(20,2,0,other),(21,2.1,0,acl(bytes.fromhex('5231000101f1')))]
    connections,_=discover(packets,'020000000001');assert len(connections[0].rows)==1
    # Mid-connection calibration identifies a channel without a connection event.
    markers=[];mid=[]
    for i,gain in enumerate((226,227,228)):
        payload=bytes((2,1,gain));t=10+i
        markers.append(dict(success=True,report_id=0x82,payload_hex=payload.hex(),before=t-.01,after=t+.01))
        mid.append((i,t,0,acl(b'\x52\x6d\x00'+payload)))
    epochs,_=discover(mid,'020000000001',allow_unidentified=True)
    mapped=calibrated_maps(epochs,markers);assert len(mapped)==1 and mapped[0][1]=={'0x6d':'0x82'}
    assert not calibrated_maps(epochs,markers[:2])
    duplicate=Connection(65);duplicate.rows=[dict(r,**{'bthci_acl.handle':'0x41'}) for r in epochs[0].rows]
    assert not calibrated_maps(epochs+[duplicate],markers)
    assert capture_diagnostics([(1,1,0,bytes.fromhex('0201080a0006000400526d00'))])['empty_writes']==1


if __name__=='__main__':
    if len(sys.argv)==2 and sys.argv[1]=='--self-test':self_test();print('PASS: HCI identity, GATT reference mapping, pcap framing');sys.exit(0)
    folder=pathlib.Path(sys.argv[1])
    try:run(folder, sys.argv[3] if len(sys.argv)==4 and sys.argv[2]=="--parser" else None)
    except Exception as error:
        (folder/'haptics.json').write_text(json.dumps(dict(status='error',candidates=[],message=str(error)),ensure_ascii=False),encoding='utf-8');sys.exit(1)
