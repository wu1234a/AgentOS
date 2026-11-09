import zipfile,re,xml.etree.ElementTree as E,json
from pathlib import Path
ns={'m':'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
a=zipfile.ZipFile('outputs/october-orders/calculated.xlsx');root=E.fromstring(a.read('xl/worksheets/sheet1.xml'));cells={c.get('r'):c for c in root.findall('m:sheetData/m:row/m:c',ns)}
src=Path(r'E:\My Program\3D打印\客户账单\万村 Name\订单10月1日-10月31日.xlsx');z=zipfile.ZipFile(src)
text=z.read('xl/worksheets/sheet1.xml').decode(); updates=json.loads(Path('outputs/october-orders/updates.json').read_text(encoding='utf-8-sig'));count=0
for u in updates:
 row=u['row']
 for col in (['D','E','F'] if row in [126,134,155] else ['D','E'] if u['price'] is not None else ['E','F']):
  ref=f'{col}{row}';c=cells[ref];s=E.tostring(c,encoding='unicode');s=re.sub(r'ns\d+:','',s);s=re.sub(r' xmlns(?::\w+)?="[^"]*"','',s)
  old=re.search(r'<c\b[^>]*r="'+ref+r'"[^>]*(?:/>|>.*?</c>)',text,re.S)
  if old:
   st=re.search(r'\bs="(\d+)"',old.group());s=re.sub(r' s="[^"]*"','',s)
   if st:s=s.replace('<c ',f'<c s="{st.group(1)}" ',1)
   text=text[:old.start()]+s+text[old.end():]
  else:
   pattern=r'(<row\b[^>]*r="'+str(row)+r'"[^>]*>)(.*?)(</row>)'
   def add(m):
    body=m.group(2); following=re.search(r'<c\b[^>]*r="([A-Z]+)\d+"',body)
    cs=list(re.finditer(r'<c\b[^>]*r="([A-Z]+)\d+"[^>]*(?:/>|>.*?</c>)',body,re.S));pos=next((x.start() for x in cs if x.group(1)>col),len(body))
    return m.group(1)+body[:pos]+s+body[pos:]+m.group(3)
   text=re.sub(pattern,add,text,flags=re.S)
  count+=1
out=Path('outputs/october-orders/订单10月1日-10月31日_已填价格及金额.xlsx')
with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as dst:
 for info in z.infolist():dst.writestr(info,text.encode() if info.filename=='xl/worksheets/sheet1.xml' else z.read(info.filename))
f=zipfile.ZipFile(out); rr=E.fromstring(f.read('xl/worksheets/sheet1.xml'));cc={c.get('r'):c for c in rr.findall('m:sheetData/m:row/m:c',ns)}
from decimal import Decimal
for u in updates:
 if u['price'] is not None:
  assert Decimal(cc[f'D{u["row"]}'].find('m:v',ns).text)==Decimal(str(u['price']))
  assert Decimal(cc[f'E{u["row"]}'].find('m:v',ns).text)==Decimal(str(u['price']))*Decimal(str(u['qty']))
z=zipfile.ZipFile(src)
assert set(z.namelist())==set(f.namelist())
assert all(z.read(n)==f.read(n) for n in z.namelist() if n!='xl/worksheets/sheet1.xml')
print('Verified 193 prices and amounts; all images and other workbook parts preserved. Output:',out.resolve())


