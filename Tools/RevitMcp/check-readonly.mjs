import {pathToFileURL} from 'node:url';
import {writeFileSync} from 'node:fs';
const root = process.argv[2];
const sdk = root + '/node_modules/@modelcontextprotocol/sdk/dist/esm';
const {Client} = await import(pathToFileURL(sdk + '/client/index.js'));
const {StdioClientTransport} = await import(pathToFileURL(sdk + '/client/stdio.js'));
const client = new Client({name:'hb-bim-readonly-check',version:'1.0.0'});
const transport = new StdioClientTransport({command:process.execPath,args:[root+'/build/index.js'],stderr:'ignore'});
const report={time:new Date().toISOString(),readOnly:true};
try {
 await client.connect(transport);
 const {tools}=await client.listTools();
 report.tools=tools.map(t=>t.name);
 report.duplicateTools=report.tools.filter((t,i,a)=>a.indexOf(t)!==i);
 report.results={};
 for(const name of ['get_project_info','get_active_view','get_all_grids']) {
   report.results[name]=await client.callTool({name,arguments:{}},undefined,{timeout:15000});
   if(report.results[name].isError) break;
 }
} catch(e) { report.error=e.message; }
finally {
 await client.close();
 writeFileSync(process.argv[3],JSON.stringify(report,null,2));
 console.log(JSON.stringify(report,null,2));
}
