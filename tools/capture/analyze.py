"""Offline analysis only. No input/haptic injection. Python 3 standard library.

ETL -> BTETLParse -> pcapng -> tshark ATT CSV -> explicitly mapped HID reports.
Never scan arbitrary byte offsets for report IDs or guess BLE attribute mappings.
"""
import argparse
import bisect
import csv
import io
import json
import pathlib
import struct
import subprocess
import sys
import unittest

FIELDS = ['frame.number', 'frame.time_epoch', 'bthci_acl.handle',
          'btatt.opcode', 'btatt.handle', 'btatt.value']


def number(value):
    return int(value, 16 if value.lower().startswith('0x') else 10)


def decode(report, data):
    """Triton output report payload WITHOUT its report ID, as carried over BLE."""
    sizes = {0x81: 7, 0x82: 3, 0x83: 9, 0x84: 8, 0x85: 3}
    if report not in sizes or len(data) < sizes[report]:
        raise ValueError('Unsupported report or truncated payload')
    if len(data) > sizes[report] and any(data[sizes[report]:]):
        raise ValueError('Unexpected nonzero trailing bytes')
    side = data[0]
    if side not in (range(3) if report == 0x82 else range(6)):
        raise ValueError('Invalid haptic side')
    result = dict(report_id=hex(report), side=side, payload_hex=data.hex())
    if report == 0x81:
        on, off, count = struct.unpack_from('<HHH', data, 1)
        result.update(kind='pulse', on_us=on, off_us=off, repeat_count=count)
    elif report == 0x82:
        cmd, gain = struct.unpack_from('<Bb', data, 1)
        result.update(kind='command', command=cmd, gain_db=gain)
        # Tick/click are firmware-defined: do not invent frequency or duration.
    elif report == 0x83:
        gain, hz, ms, lfo, depth = struct.unpack_from('<bHHHB', data, 1)
        result.update(kind='tone', gain_db=gain, hz=hz, duration_ms=ms,
                      lfo_hz=lfo, lfo_depth=depth)
    elif report == 0x84:
        gain, ms, begin, end = struct.unpack_from('<bHHH', data, 1)
        result.update(kind='sweep', gain_db=gain, duration_ms=ms,
                      start_hz=begin, end_hz=end)
    else:
        script, gain = struct.unpack_from('<Bb', data, 1)
        result.update(kind='script', script_id=script, gain_db=gain)
    return result


def analyze(rows, config, inputs):
    if config.get('verified_sc2') is not True:
        raise ValueError('First verify the SC2 connection and output-report attribute mapping.')
    connection = number(str(config['connection_handle']))
    mapping = {number(k): number(str(v)) for k, v in config['output_attributes'].items()}
    includes_id = config.get('value_includes_report_id', False)
    if not isinstance(includes_id, bool):
        raise ValueError('value_includes_report_id must be boolean')
    inputs = sorted(inputs, key=lambda r: float(r['unix_seconds']))
    times = [float(r['unix_seconds']) for r in inputs]
    found, rejected = [], []
    for row in rows:
        try:
            if number(row['bthci_acl.handle']) != connection:
                continue
            opcode = number(row['btatt.opcode'])
            if opcode not in (0x12, 0x52):
                # Prepared/fragmented writes are not silently concatenated.
                if opcode in (0x16, 0x18):
                    rejected.append(dict(frame=row['frame.number'], reason='Prepared/execute write needs separate reassembly'))
                continue
            attribute = number(row['btatt.handle'])
            if attribute not in mapping:
                continue
            report = mapping[attribute]
            data = bytes.fromhex(row['btatt.value'].replace(':', ''))
            if includes_id:
                if not data or data[0] != report:
                    raise ValueError('Report ID disagrees with verified mapping')
                data = data[1:]
            item = decode(report, data)
            left = config.get('capture_side', 'left') == 'left'
            if item['side'] != ((1 if left else 0) if report == 0x81 else (0 if left else 1)):
                continue
            time = float(row['frame.time_epoch'])
            item.update(unix_seconds=time, frame=row['frame.number'], attribute=hex(attribute))
            j = bisect.bisect_left(times, time)
            candidates = [k for k in (j-1, j) if 0 <= k < len(times)]
            if candidates:
                k = min(candidates, key=lambda k: abs(times[k]-time))
                if abs(times[k]-time) <= .050:
                    item.update(nearest_input=inputs[k], output_minus_input_ms=round((time-times[k])*1000, 3))
            found.append(item)
        except (ValueError, KeyError, TypeError) as e:
            rejected.append(dict(frame=row.get('frame.number', '?'), reason=str(e)))
    found.sort(key=lambda x: x['unix_seconds'])
    last_by_side = {}
    for item in found:
        side = item['side']
        if side in last_by_side:
            item['interval_ms_same_side'] = round((item['unix_seconds']-last_by_side[side])*1000, 3)
        last_by_side[side] = item['unix_seconds']
    return dict(status='decoded' if found else 'inconclusive',
                note='Host ATT writes; attribution to Steam and causal timing require controlled capture. No output means inconclusive, not no haptics.',
                left_haptic_reports=found, rejected=rejected)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    export = sub.add_parser('export')
    export.add_argument('--tshark', required=True)
    export.add_argument('--pcap', type=pathlib.Path, required=True)
    export.add_argument('--out', type=pathlib.Path, required=True)
    parse = sub.add_parser('decode')
    parse.add_argument('--att', type=pathlib.Path, required=True)
    parse.add_argument('--map', type=pathlib.Path, required=True)
    parse.add_argument('--input', type=pathlib.Path, required=True)
    parse.add_argument('--out', type=pathlib.Path, required=True)
    sub.add_parser('self-test')
    args = parser.parse_args()
    if args.action == 'self-test':
        return 0 if unittest.TextTestRunner().run(unittest.defaultTestLoader.loadTestsFromTestCase(Tests)).wasSuccessful() else 1
    if args.action == 'export':
        cmd = [args.tshark, '-r', str(args.pcap), '-Y', 'btatt', '-T', 'fields',
               '-E', 'header=y', '-E', 'separator=,', '-E', 'quote=d', '-E', 'occurrence=a']
        for field in FIELDS:
            cmd += ['-e', field]
        # Multiple values remain comma-separated inside a quoted cell and will be
        # rejected by the decoder rather than accidentally assigned to one PDU.
        with args.out.open('w', encoding='utf-8', newline='') as out:
            subprocess.run(cmd, stdout=out, check=True)
    else:
        with args.att.open(encoding='utf-8-sig', newline='') as att, args.input.open(encoding='utf-8-sig', newline='') as raw:
            result = analyze(csv.DictReader(att), json.loads(args.map.read_text(encoding='utf-8-sig')), list(csv.DictReader(raw)))
        args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
        print(result['status'], len(result['left_haptic_reports']), 'left/both reports;', len(result['rejected']), 'rejected')
    return 0


class Tests(unittest.TestCase):
    def test_wave_layouts(self):
        self.assertEqual(decode(0x82, bytes.fromhex('0101f1'))['gain_db'], -15)
        self.assertEqual(decode(0x83, bytes.fromhex('01f1a0000500000000'))['duration_ms'], 5)
        self.assertEqual(decode(0x81, bytes.fromhex('016400c8000300'))['repeat_count'], 3)
        self.assertEqual(decode(0x84, bytes.fromhex('01f114006400a000'))['end_hz'], 160)
    def test_report_specific_sides(self):
        for report, left, right, payload in [(0x81,1,0,'900100000100'),(0x82,0,1,'01f4'),(0x83,0,1,'f1a0000500000000')]:
            config=dict(verified_sc2=True,connection_handle='0x40',output_attributes={'0x31':hex(report)})
            def row(side):
                return dict(zip(FIELDS,['1','1','0x40','0x52','0x31',bytes([side]).hex()+payload]))
            result=analyze([row(left),row(right),row(2)],config,[])
            self.assertEqual([r['side'] for r in result['left_haptic_reports']],[left])

    def test_right_source(self):
        for report,left,right,payload in [(0x81,1,0,'900100000100'),(0x82,0,1,'01fd'),(0x83,0,1,'f1a0000500000000')]:
            cfg=dict(verified_sc2=True,connection_handle='0x40',output_attributes={'0x31':hex(report)},capture_side='right')
            rows=[dict(zip(FIELDS,['1','1','0x40','0x52','0x31',bytes([side]).hex()+payload])) for side in (left,right,2)]
            result=analyze(rows,cfg,[])
            self.assertEqual([r['side'] for r in result['left_haptic_reports']],[right])

    def test_validation(self):
        for report, data in [(0x82,b'\x01'), (0x83,b'\x01\xf1'), (0x82,bytes.fromhex('0301f1')), (0x82,bytes.fromhex('0101f1aa'))]:
            with self.assertRaises(ValueError): decode(report, data)
    def test_filter_and_alignment(self):
        config = dict(verified_sc2=True, connection_handle='0x40', output_attributes={'0x31':'0x82'})
        def row(t, payload, handle='0x40'):
            return dict(zip(FIELDS, ['1',str(t),handle,'0x52','0x31',payload]))
        rows = [row(1.01,'0001f1'),row(1.03,'0101f1'),row(1.04,'0001f1','0x41'),row(1.07,'0001f1')]
        result = analyze(rows,config,[dict(unix_seconds='1.0',touch='1')])
        self.assertEqual(len(result['left_haptic_reports']),2)
        self.assertEqual(result['left_haptic_reports'][0]['output_minus_input_ms'],10)
        self.assertEqual(result['left_haptic_reports'][1]['interval_ms_same_side'],60)
        self.assertNotIn('nearest_input',result['left_haptic_reports'][1])
        self.assertEqual(analyze([],config,[])['status'],'inconclusive')
        config['verified_sc2']=False
        with self.assertRaises(ValueError): analyze(rows,config,[])


if __name__ == '__main__':
    sys.exit(main())
