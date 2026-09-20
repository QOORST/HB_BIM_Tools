import {pathToFileURL} from 'node:url';
import {writeFileSync} from 'node:fs';
const root = process.argv[2];
const sdk = root + '/node_modules/@modelcontextprotocol/sdk/dist/esm';
const {Client} = await import(pathToFileURL(sdk + '/client/index.js'));
const {StdioClientTransport} = await import(pathToFileURL(sdk + '/client/stdio.js'));
const client = new Client({name:'hb-bim-mep-readonly-check',version:'1.0.0'});
const transport = new StdioClientTransport({command:process.execPath,args:[root+'/build/index.js'],stderr:'ignore'});
const report={time:new Date().toISOString(),readOnly:true,results:{}};
try {
  await client.connect(transport);
  report.results.activeView=await client.callTool({name:'get_active_view',arguments:{}},undefined,{timeout:15000});
  for(const category of ['OST_PipeCurves','OST_DuctCurves','OST_Conduit','OST_CableTray','OST_PipeFitting','OST_MechanicalEquipment']) {
    report.results[category]=await client.callTool({name:'query_elements',arguments:{category,maxCount:5}},undefined,{timeout:15000});
  }
} catch(e) { report.error=e.message; }
finally {
  await client.close();
  writeFileSync(process.argv[3],JSON.stringify(report,null,2));
  console.log(JSON.stringify(report,null,2));
}
