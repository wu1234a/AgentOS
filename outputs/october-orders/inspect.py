import zipfile,xml.etree.ElementTree as E,json
from pathlib import Path
n={'m':'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
base=Path(r'E:\My Program\3D打印\客户账单\万村 Name')
out=[]
for name in ['价格表.xlsx','订单10月1日-10月31日.xlsx']:
 z=zipfile.ZipFile(base/name); ss=[]
 if 'xl/sharedStrings.xml' in z.namelist():
  ss=[''.join(e.itertext()) for e in E.fromstring(z.read('xl/sharedStrings.xml')).findall('m:si',n)]
 print(name)
 print([(s.get('name'),s.get('sheetId')) for s in E.fromstring(z.read('xl/workbook.xml')).findall('m:sheets/m:sheet',n)])
 sheets={}
 for p in z.namelist():
  if p.startswith('xl/worksheets/sheet') and p.endswith('.xml'):
   root=E.fromstring(z.read(p));rows=[]
   for r in root.findall('m:sheetData/m:row',n):
    vals={}
    for c in r.findall('m:c',n):
     v=c.find('m:v',n); t=c.get('t'); value=v.text if v is not None else ''
     if t=='s':value=ss[int(value)] if value else ''
     if t=='inlineStr':value=''.join(c.find('m:is',n).itertext())
     f=c.find('m:f',n)
     if value or f is not None:vals[c.get('r')]=value if f is None else {'value':value,'formula':f.text}
    if vals:rows.append(vals)
   sheets[p]=rows
   print(p,len(rows),json.dumps(rows[:2]+rows[-2:],ensure_ascii=True))
 out.append({'name':name,'sheets':sheets})
Path('outputs/october-orders/source.json').write_text(json.dumps(out,ensure_ascii=False),encoding='utf-8')

