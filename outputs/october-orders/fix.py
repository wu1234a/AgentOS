import zipfile,re,posixpath
p='outputs/october-orders/preview-source.xlsx'
z=zipfile.ZipFile(p);names=set(z.namelist());data={n:z.read(n) for n in names};z.close()
for n,b in data.items():
 if n.endswith('.rels'):
  parent=posixpath.dirname(posixpath.dirname(n))
  def keep(m):
   t=re.search(r'Target="([^"]+)"',m.group()).group(1); resolved=posixpath.normpath(posixpath.join(parent,t)) if not t.startswith('/') else t[1:]
   return m.group() if resolved in names or 'TargetMode="External"' in m.group() else ''
  data[n]=re.sub(r'<Relationship\s[^>]*/>',keep,b.decode()).encode()
with zipfile.ZipFile(p,'w',zipfile.ZIP_DEFLATED) as z:
 for n,b in data.items():z.writestr(n,b)
