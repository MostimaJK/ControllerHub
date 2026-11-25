const nav=document.querySelector("nav"), marker=nav.querySelector("i");
const buttons=["gamepad-2","activity","git-pull-request","sliders-horizontal","folders","settings"].map((name,index)=>{
  const button=document.createElement("button"),icon=document.createElement("span");
  icon.className="nav-icon";icon.style.maskImage="url(icons/"+name+".svg)";icon.setAttribute("aria-hidden","true");button.append(icon,document.createTextNode(""));
  button.onclick=()=>{
    marker.style.transform="translateY("+button.offsetTop+"px)";
    window.hub.navigate(index);
  };nav.append(button);return button;
});
window.hub.onState(({labels,index,language})=>{
  document.documentElement.lang=language;
  buttons.forEach((button,i)=>{button.lastChild.textContent=labels[i];button.setAttribute("aria-current",index===i?"page":"false");});
  if(buttons[index])marker.style.transform="translateY("+buttons[index].offsetTop+"px)";
});
let currentRevision=0, readings=[], badges=[];
function updateBadge(node,state,label){
  if(node.dataset.state===state && node.lastChild.textContent===label)return;
  node.dataset.state=state;
  node.firstChild.textContent=({ready:"✓",warning:"!",input:"i",idle:"•"})[state];
  node.lastChild.textContent=label;
}
const main=document.querySelector("main");
function render(n){
  if(!n)return document.createDocumentFragment();
  const el=tag=>document.createElement(tag);
  const d=n.data;
  const action=value=>window.hub.action({revision:currentRevision,id:n.id,value});
  let e;
  switch(n.kind){
    case "connectionSteps":
      e=el("ol");e.className="connection-steps";
      for(const {State,Label,Symbol} of n.steps){
        const row=el("li"), icon=el("b");
        row.dataset.state=State;
        if(State==="active")row.setAttribute("aria-current","step");
        icon.setAttribute("aria-hidden","true");icon.textContent=Symbol;
        row.append(icon,document.createTextNode(Label));e.append(row);
      }
      break;
    case "connectionStatus":{
      e=el("div");e.className="connection-status";e.setAttribute("role","status");
      const icon=el("b");icon.setAttribute("aria-hidden","true");e.append(icon,el("span"));
      badges[n.slot]=e;updateBadge(e,n.state,n.label);break;
    }
    case "text":
      e=el("span");if(n.hint){e.className="connection-hint";e.setAttribute("role","status");}e.textContent=n.text;e.style.fontSize=n.size+"px";if(n.bold)e.style.fontWeight="650";readings[n.live]=e;break;
    case "art":e=el("img");e.src=n.asset==="DualSense" && location.protocol==="file:" ? "../src/ControllerFusion.App/Assets/DualSense.png" : n.asset+".png";e.alt="";break;
    case "layout":
      e=el("div");e.className=n.columns?"columns":n.horizontal?"row":"stack";
      if(n.columns)e.dataset.cols=n.columns;
      if(n.columns)e.style.gridTemplateColumns="repeat("+n.columns+", minmax(0,1fr))";
      for(const child of n.children){
        if(!child.node)continue;
        const c=render(child.node);
        if(n.grid && n.columns){c.style.gridColumn=child.col;c.style.gridRow=child.row;}
        e.append(c);
      }
      break;
    case "group":case "card":
      e=el("div");e.className=n.kind;n.children.forEach(c=>e.append(render(c)));break;
    case "Button":
      e=el("button");if(typeof d.content==="string")e.textContent=d.content;else e.append(render(d.content));
      e.className=d.primary?"primary":d.selected?"selected":"";e.onclick=()=>action(null);break;
    case "ComboBox":
      e=el("select");d.options.forEach(x=>e.add(new Option(x)));e.selectedIndex=d.value;e.onchange=()=>action(e.selectedIndex);break;
    case "CheckBox":{
      e=el("label");const input=el("input");input.type="checkbox";input.checked=d.value;input.disabled=n.disabled;input.onchange=()=>action(input.checked);e.append(input,document.createTextNode(d.label));break;}
    case "Slider":e=el("input");Object.assign(e,{type:"range",min:d.min,max:d.max,step:d.step,value:d.value});e.oninput=()=>action(Number(e.value));break;
    case "TextBox":e=el(d.readOnly?"textarea":"input");e.value=d.value;e.readOnly=d.readOnly;e.oninput=()=>action(e.value);break;
    default:return document.createDocumentFragment();
  }
  if(n.disabled)e.disabled=true;
  return e;
}
let activePage=-1;
window.hub.onState(state=>{
  main.dataset.page=state.index;
  currentRevision=state.revision;readings=[];badges=[];
  main.replaceChildren(render(state.body));
  if(activePage!==state.index){main.scrollTop=0;}
  activePage=state.index;
});
window.hub.readings(data=>{
  if(data.revision!==currentRevision)return;
  data.badges?.forEach((b,i)=>{if(badges[i])updateBadge(badges[i],b.state,b.label);});
  data.text.forEach((value,i)=>{if(readings[i] && readings[i].textContent!==value)readings[i].textContent=value;});
});
