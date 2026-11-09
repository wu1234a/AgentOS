exec(open('outputs/october-orders/match.py',encoding='utf-8-sig').read().split("print('matched'")[0])
updates=[]
from collections import defaultdict
choices=defaultdict(list)
for rr in p[1:]:
 rn=re.search(r'\d+',next(iter(rr))).group();choices[img(next(iter(rr.values())))].append((rr.get('B'+rn),rr.get('C'+rn,'')))
for r in o[1:]:
 row=int(re.search(r'\d+',next(iter(r))).group()); id=img(r.get('B'+str(row))); opts=choices[id]; note=r.get('F'+str(row),''); hits=[v for v,s in opts if s and s==note]; value=hits[0] if len(hits)==1 else opts[0][0] if len(opts)==1 else None; price=float(value) if value is not None else None; qty=float(r['C'+str(row)])
 updates.append({'row':row,'price':price,'qty':qty})
Path('outputs/october-orders/updates.json').write_text(json.dumps(updates),encoding='utf-8')
z=zipfile.ZipFile(Path(r'E:\My Program\3D打印\客户账单\万村 Name\订单10月1日-10月31日.xlsx'))
with zipfile.ZipFile('outputs/october-orders/preview-source.xlsx','w',zipfile.ZIP_DEFLATED) as out:
 for name in z.namelist():
  if name.startswith(('xl/media/','xl/drawings/')) or 'cellimage' in name.lower():continue
  data=z.read(name)
  if name=='xl/worksheets/sheet1.xml':
   text=data.decode('utf-8');text=re.sub(r'<c\b[^>]*r="B\d+"[^>]*>.*?</c>',lambda m:re.sub(r'<f>.*?</f>|<v>.*?</v>','',m.group()),text,flags=re.S);data=text.encode('utf-8')
  out.writestr(name,data)

