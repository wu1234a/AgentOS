import json,re,zipfile,xml.etree.ElementTree as E,hashlib
from pathlib import Path
d=json.loads(Path('outputs/october-orders/source.json').read_text(encoding='utf-8')); p=d[0]['sheets']['xl/worksheets/sheet19.xml'];o=d[1]['sheets']['xl/worksheets/sheet1.xml']
def img(v):return re.search(r'ID_[A-F0-9]+',str(v)).group() if 'ID_' in str(v) else None
prices={img(next(iter(r.values()))):(r.get('B'+re.search(r'\d+',next(iter(r))).group()),next(iter(r))) for r in p[1:]}
missing=[]
for r in o[1:]:
 b=next((v for k,v in r.items() if k.startswith('B')),None);id=img(b)
 if id not in prices:missing.append(r)
print('matched',len(o)-1-len(missing),'missing',len(missing));print(json.dumps(missing,ensure_ascii=False))
