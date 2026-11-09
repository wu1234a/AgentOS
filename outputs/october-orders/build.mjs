import fs from 'node:fs/promises';
import {FileBlob,SpreadsheetFile} from '@oai/artifact-tool';
const dir='E:/Projects/AgentOS/outputs/october-orders';
const wb=await SpreadsheetFile.importXlsx(await FileBlob.load(dir+'/preview-source.xlsx'));
const sh=wb.worksheets.getItemAt(0);
await fs.writeFile(dir+'/before.png',new Uint8Array(await (await wb.render({sheetName:'Sheet1',range:'A1:F5',scale:1})).arrayBuffer()));
const updates=JSON.parse((await fs.readFile(dir+'/updates.json','utf8')).replace(/^\uFEFF/,''));
for(const {row,price} of updates){
 if(price!==null) sh.getRange(`D${row}`).values=[[price]];
 else sh.getRange(`F${row}`).values=[[ [126,134,155].includes(row)?'待确认尺寸及单价':'价格表缺少单价，待确认']];
 sh.getRange(`E${row}`).formulas=[[`=IF(D${row}="","",ROUND(C${row}*D${row},2))`]];
}
for(const [row,size] of [[126,'原尺寸'],[134,'15cm'],[155,'12cm']]) sh.getRange(`F${row}`).values=[[size]];
sh.getRange('D2:E194').setNumberFormat('0.00');
wb.recalculate();
console.log((await wb.inspect({kind:'table',range:'Sheet1!C2:F5',include:'values,formulas',maxChars:1800})).ndjson);
console.log((await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#NUM!',options:{useRegex:true,maxResults:20},maxChars:1000})).ndjson);
await fs.writeFile(dir+'/after.png',new Uint8Array(await (await wb.render({sheetName:'Sheet1',range:'C124:F126',scale:1})).arrayBuffer()));
await(await SpreadsheetFile.exportXlsx(wb)).save(dir+'/calculated.xlsx');
console.log(JSON.stringify({priced:updates.filter(x=>x.price!==null).length,pending:updates.filter(x=>x.price===null).map(x=>x.row),knownTotal:updates.reduce((s,x)=>s+(x.price===null?0:Math.round(x.qty*x.price*100)/100),0)}));

