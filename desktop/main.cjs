const {app, BrowserWindow, ipcMain, dialog, Tray, Menu} = require("electron");
const {spawn} = require("node:child_process");
const readline = require("node:readline");
const path = require("node:path");

app.whenReady().then(async () => {
  const window = new BrowserWindow({icon:path.join(__dirname,"../src/ControllerFusion.App/app.ico"),title:"ControllerHub", width:1240,height:860,minWidth:1020,minHeight:740,
    backgroundColor:"#f8f9fc", autoHideMenuBar:true, webPreferences:{preload:path.join(__dirname,"preload.cjs")}});
  window.on("page-title-updated", event => event.preventDefault());
  window.setTitle(`ControllerHub v${app.getVersion()}`);
  window.webContents.on("will-navigate", event => event.preventDefault());
  window.webContents.setWindowOpenHandler(() => ({action:"deny"}));
  await window.loadFile(path.join(__dirname,"index.html"));

  const child = spawn(process.env.HUB_SDK,[process.env.HUB_APP,"--desktop-model"],{windowsHide:true,stdio:["pipe","pipe","inherit"]});
  let finished=false, tray=null, settings=null;
  window.on("minimize",()=>{
    if(!settings?.tray)return;
    tray ??= new Tray(path.join(__dirname,"../src/ControllerFusion.App/app.ico"));
    const restore=()=>{window.show();window.restore();window.focus();};
    tray.setToolTip("ControllerHub");
    tray.removeAllListeners("click");
    tray.on("click",restore);
    tray.setContextMenu(Menu.buildFromTemplate([{label:"ControllerHub",click:restore},{role:"quit"}]));
    window.hide();
  });
  const send = message => {if(child.stdin.writable) child.stdin.write(message+"\n");};
  child.stdin.on("error",()=>{});
  ipcMain.on("navigate",(event,index)=>{
    if(event.sender===window.webContents && Number.isInteger(index) && index>=0 && index<6) send("page "+index);
  });
  ipcMain.on("action",(event,command)=>{
    if(event.sender===window.webContents && Number.isInteger(command.id) && Number.isInteger(command.revision))
      send(JSON.stringify(command));
  });
  readline.createInterface({input:child.stdout}).on("line",line=>{
    if(line.startsWith("LIVE ") && !window.isDestroyed()) window.webContents.send("readings",JSON.parse(line.slice(5)));
    if(line.startsWith("HUB ") && !window.isDestroyed()) {settings=JSON.parse(line.slice(4));window.webContents.send("state",settings);};
  });
  window.on("close",event=>{if(!finished){event.preventDefault();send("close");}});
  function finish(error){if(finished)return; finished=true;tray?.destroy();if(error)dialog.showErrorBox("ControllerHub",error);window.destroy();app.quit();}
  child.on("error",e=>finish(e.message));
  child.on("exit",code=>finish(code ? "Controller engine exited ("+code+")." : null));
});
