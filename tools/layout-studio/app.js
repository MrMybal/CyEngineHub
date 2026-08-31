(function(){
"use strict";
var KEY="cyenginehub-layout-studio-v1",W=1440,H=900,seq=0,selected=null,active="overview",undoStack=[],redoStack=[],zoom=.72,editMark=false,timer;
var $=function(id){return document.getElementById(id)},board=$("board"),stage=$("stage");
function id(prefix){seq++;return prefix+"-"+Date.now().toString(36)+"-"+seq}
function block(type,title,body,x,y,w,h,tags,instruction){return{id:id(type),type:type,title:title,body:body||"",x:x,y:y,w:w,h:h,z:seq,tags:tags||[],instruction:instruction||""}}
function shell(title,sub){return[
 block("sidebar","CYENGINEHUB","Vue d’ensemble\nMoteurs\nProjets\nBibliothèque\nGit\nPlugins\nExtensions Hub\nStockage\n\nTâches\nLogs\nParamètres",0,0,226,900,["navigation"]),
 block("hero",title,sub,226,0,1214,94,["en-tête"]),
 block("status","CONNEXIONS","EPIC ●   FAB ●   GIT ●   UNITY ●",1052,24,354,52,["statut global"])
]}
function cards(target,data,y){
 data.forEach(function(d,i){target.push(block("card",d[0],d[1],260+(i%3)*335,y+Math.floor(i/3)*245,315,220,d[2]||[]))})
}
function defaults(){
 seq=0;var p=[],a;
 a=shell("Vue d’ensemble","Tous vos moteurs, projets et téléchargements au même endroit.");
 cards(a,[["13 moteurs","Installations détectées\n4 providers actifs",["moteurs"]],["36 projets","Tous moteurs confondus\n5 favoris",["projets"]],["8 téléchargements","2 actifs · 6 en attente",["tâches"]]],130);
 a.push(block("panel","Reprendre rapidement","Beyond — Unreal Engine 5.5\nNeon District — Unity 6 LTS\nTiny Odyssey — Godot 4.4",260,390,650,210,["récents"]),block("panel","Activité","Fantasy Forest : 68%\nCompilation CyBlend : terminée\nScan : il y a 4 min",930,390,360,210,["activité"]),block("note","Exemple d’annotation","Déplace cette note et écris ce que tu veux changer.",760,650,380,150,["à modifier"],"Remplacer ce bloc par un résumé compact."));
 p.push({id:"overview",name:"Vue d’ensemble",description:"Dashboard global",comment:"",elements:a});

 a=shell("Moteurs","Providers, installations, plugins, build et toolchains.");
 a.push(block("tabs","MOTEURS","Unreal Engine   Unity   Godot   CryEngine   +",260,120,1080,66,["onglets"]),block("toolbar","Unreal Engine","Rechercher…     Cartes | Liste     + Ajouter",260,205,1080,74,["actions"]));
 cards(a,[["Unreal Engine 5.5","G:\\UnrealEngine\\UE_5.5\nHealthy · EpicBinary\n47 plugins · 12 projets",["UE 5.5","installé"]],["Unity 6 LTS","D:\\Unity\\6000.0.32f1\nHealthy · Unity Hub\n24 packages · 6 projets",["unity","installé"]],["Godot 4.4","D:\\Godot\\Godot_v4.4.exe\nHealthy · Standard\n8 addons · 4 projets",["godot","installé"]],["CryEngine 5.7","E:\\CryEngine\\5.7\nAttention · toolchain\n5 plugins · 2 projets",["cryengine"]]],310);
 a.push(block("note","À toi de jouer","Déplace les cartes, rends-les carrées ou indique ce qui doit disparaître.",930,770,360,110,["annotation"]));
 p.push({id:"engines",name:"Moteurs",description:"Installations et profils",comment:"",elements:a});

 a=shell("Projets","Tous les projets classés par moteur, dossier ou collection.");
 a.push(block("tabs","PROJETS","Unreal Engine   Unity   Godot   CryEngine",260,120,790,66,["onglets"]),block("toolbar","Actions","+ Nouveau projet   Scanner   Ajouter   Cartes | Liste",260,205,1080,74,["création","filtres"]));
 cards(a,[["Beyond","Unreal Engine 5.5\nJ:\\AtlasXR\\Beyond\nC++ · 18 plugins",["unreal","favori"]],["Neon District","Unity 6 LTS\nD:\\Games\\NeonDistrict\nURP · 24 packages",["unity"]],["Tiny Odyssey","Godot 4.4\nD:\\Indie\\TinyOdyssey\nGDScript · Steam",["godot"]],["Atlas VR","Unreal Engine 5.4\nJ:\\AtlasXR\\AtlasVR\nC++ · OpenXR",["unreal","VR"]],["Forest Builder","Unity 2022 LTS\nE:\\Prototypes\\Forest\nHDRP",["unity"]],["Cry Valley","CryEngine 5.7\nE:\\CryProjects\\Valley\nSandbox",["cryengine"]]],310);
 p.push({id:"projects",name:"Projets",description:"Cartes et profils projet",comment:"",elements:a});

 a=shell("Bibliothèque","Assets Fab, Unity Asset Store et packages locaux.");
 a.push(block("tabs","SOURCES","Tout   Fab   Unity Asset Store   Packages locaux",260,120,880,66,["sources"]),block("toolbar","Rechercher et classer","Rechercher…   Type ▾   Moteur ▾   Collection ▾   Tri ▾",260,205,1080,74,["recherche","tri"]));
 cards(a,[["Abandoned Factory","Fab · Environnement\nUE 5.0–5.6\nMiniature async",["fab"]],["Fantasy Forest","Package local · FBX\n42 meshes · 18 textures",["local","FBX"]],["Sci-Fi Props Pack","Unity Asset Store\nURP · HDRP\nTéléchargé",["unity"]],["Knight Animations","Fab · Animations\nUnreal · Unity",["fab"]],["Medieval Village","Package local · glTF\nArchitecture",["local"]],["Surface Materials","Fab · Matériaux\nUE 5.3–5.6",["fab"]]],310);
 p.push({id:"library",name:"Bibliothèque",description:"Assets multi-sources",comment:"",elements:a});

 a=shell("Plugins & packages","Inventaire par moteur, projet et compatibilité.");
 a.push(block("tabs","MOTEURS","Unreal Engine   Unity   Godot   CryEngine",260,120,800,66,["filtres"]),block("toolbar","Catalogue","Rechercher…   Installés ▾   Compatibilité ▾   + Package local",260,205,1080,74,["recherche"]));
 cards(a,[["CyBlend","Plugin Unreal\n5.3 · 5.4 · 5.5\nInstallé sur 2 moteurs",["unreal"]],["Dialogue System","Package Unity\nVersion 3.2.1\nNeon District",["unity"]],["Terrain Toolkit","Addon Godot\nVersion 1.8\nMise à jour disponible",["godot"]],["Godot Steam","Addon Godot\nVersion 4.12\nInstallé",["godot"]]],310);
 p.push({id:"plugins",name:"Plugins & packages",description:"Inventaire multi-moteur",comment:"",elements:a});

 a=shell("Extensions Hub","Outils installés dans CyEngineHub.");
 a.push(block("toolbar","Extensions installées","Rechercher…   Toutes ▾   + Installer",260,125,1080,74,["actions"]));
 cards(a,[["Asset Bridge","Transfert entre moteurs\nFenêtre dédiée · paramètres",["conversion"]],["Asset Downgrader","Outil interne CyEngineHub\nFenêtre dédiée Unreal\nAucun plugin Fab",["interne"]],["Build Monitor","Surveillance des builds\nNotifications et logs RAW",["build"]]],235);
 a.push(block("panel","API Extensions","Points d’injection UI · fenêtres dédiées · actions contextuelles · paramètres · permissions",260,505,1030,150,["SDK","API"]),block("note","Principe","La liste reste ici. Une extension peut ouvrir sa fenêtre ou ses réglages.",310,700,440,120,["rappel"]));
 p.push({id:"extensions",name:"Extensions Hub",description:"Outils et fenêtres dédiées",comment:"",elements:a});

 a=shell("Paramètres","Configuration complète de CyEngineHub.");
 a.push(block("sidebar","PARAMÈTRES","Connexions\nMoteurs\nProjets\nBibliothèque\nTéléchargements\nStockage\nExtensions\nInterface\nLogs & diagnostic\nAvancé",260,125,250,700,["navigation verticale"]),block("tabs","CONNEXIONS","Epic / Fab   Git   Unity   Autres services",535,125,805,66,["connexion"]),block("panel","Epic & Fab","Connecté en direct · session chiffrée\nCompte : demo@local\nRenouveler · Déconnecter",535,215,380,225,["connecté"]),block("panel","Git","GitHub configuré\nGitLab non configuré\nAjouter une connexion",935,215,380,225,["git"]),block("panel","Unity","Unity ID : connexion requise\nAsset Store : avertissement",535,465,380,190,["unity"]),block("panel","Comportement","Démarrage auto : non\nHors-ligne : auto\nVérifier : oui",935,465,380,190,["application"]),block("note","Tous les réglages ici","Aucun bouton de réglages dans les pages métier.",820,700,450,120,["architecture"],"Conserver cette règle partout."));
 p.push({id:"settings",name:"Paramètres",description:"Réglages plein écran",comment:"",elements:a});

 a=shell("Logs RAW","Événements bruts exportables pour diagnostic.");
 a.push(block("toolbar","Flux","Pause   Effacer   Niveau ▾   Source ▾   Rechercher   Exporter",260,125,1080,74,["contrôles"]),block("panel","Journal brut","[12:48:01.142] INF App       Startup completed\n[12:48:01.527] INF Discovery 13 engines, 36 projects\n[12:48:02.011] INF FabAuth   Session restored\n[12:48:02.450] WRN Unity     Token unavailable\n[12:48:03.084] DBG Thumbs    Queue size: 24\n[12:48:04.007] ERR Build     MSVC 14.38 missing",260,225,1080,480,["RAW","diagnostic"]),block("status","1 erreur · 1 avertissement","Copier la sélection   Ouvrir le dossier des logs",260,730,1080,74,["résumé"]));
 p.push({id:"logs",name:"Logs RAW",description:"Diagnostic exportable",comment:"",elements:a});
 return p
}
function copy(o){return JSON.parse(JSON.stringify(o))}
var pristine=defaults(),state=load()||{version:1,pages:copy(pristine),activePageId:"overview",zoom:72,grid:true};
active=state.pages.some(function(p){return p.id===state.activePageId})?state.activePageId:state.pages[0].id;
zoom=Math.max(.35,Math.min(1,Number(state.zoom||72)/100));
function page(){return state.pages.find(function(p){return p.id===active})}
function chosen(){return page().elements.find(function(e){return e.id===selected})}
function load(){try{var raw=localStorage.getItem(KEY);if(!raw)return null;var s=JSON.parse(raw);return s&&Array.isArray(s.pages)?s:null}catch(e){return null}}
function save(){state.activePageId=active;state.zoom=Math.round(zoom*100);state.grid=$("gridToggle").checked;localStorage.setItem(KEY,JSON.stringify(state));$("saveState").textContent="Sauvegardé à "+new Date().toLocaleTimeString("fr-FR")}
function checkpoint(){undoStack.push(JSON.stringify(state));if(undoStack.length>80)undoStack.shift();redoStack=[];historyButtons()}
function restore(raw){state=JSON.parse(raw);active=state.activePageId||state.pages[0].id;selected=null;zoom=Number(state.zoom||72)/100;$("gridToggle").checked=state.grid!==false;render()}
function undo(){if(!undoStack.length)return;redoStack.push(JSON.stringify(state));restore(undoStack.pop());save();historyButtons()}
function redo(){if(!redoStack.length)return;undoStack.push(JSON.stringify(state));restore(redoStack.pop());save();historyButtons()}
function historyButtons(){$("undoBtn").disabled=!undoStack.length;$("redoBtn").disabled=!redoStack.length}
function toast(text){clearTimeout(timer);$("toast").textContent=text;$("toast").classList.add("show");timer=setTimeout(function(){$("toast").classList.remove("show")},2200)}
function clamp(n,a,b){return Math.max(a,Math.min(b,n))}
function snap(n){return $("gridToggle").checked?Math.round(n/10)*10:Math.round(n)}
function maxZ(){return page().elements.reduce(function(m,e){return Math.max(m,e.z||0)},0)}
function minZ(){return page().elements.reduce(function(m,e){return Math.min(m,e.z||0)},0)}

function render(){
 renderPages();renderBoard();renderInspector();renderZoom();historyButtons();
 $("activePageName").textContent=page().name;$("pageComment").value=page().comment||""
}
function renderPages(){
 var list=$("pageList");list.innerHTML="";
 state.pages.forEach(function(p){
  var b=document.createElement("button");b.className="page-button"+(p.id===active?" active":"");b.textContent=p.name;
  var s=document.createElement("span");s.textContent=p.description||p.elements.length+" éléments";b.appendChild(s);
  b.onclick=function(){active=p.id;selected=null;render();save()};list.appendChild(b)
 })
}
function renderBoard(){
 board.innerHTML="";
 page().elements.slice().sort(function(a,b){return a.z-b.z}).forEach(function(e){
  var n=document.createElement("article");n.className="block "+e.type+(e.id===selected?" selected":"")+(e.instruction.trim()?" has-instruction":"");
  n.dataset.id=e.id;n.style.cssText="left:"+e.x+"px;top:"+e.y+"px;width:"+e.w+"px;height:"+e.h+"px;z-index:"+e.z;
  var t=document.createElement("div");t.className="block-title";t.textContent=e.title;n.appendChild(t);
  if(e.body){var b=document.createElement("div");b.className="block-body";b.textContent=e.body;n.appendChild(b)}
  if(e.tags&&e.tags.length){var tags=document.createElement("div");tags.className="block-tags";e.tags.forEach(function(tag){var x=document.createElement("span");x.className="block-tag";x.textContent=tag;tags.appendChild(x)});n.appendChild(tags)}
  var h=document.createElement("span");h.className="resize-handle";h.title="Redimensionner";n.appendChild(h);
  n.onpointerdown=function(ev){moveStart(ev,e,n)};h.onpointerdown=function(ev){resizeStart(ev,e,n)};
  n.ondblclick=function(ev){ev.preventDefault();select(e.id,n);$("propInstruction").focus()};
  board.appendChild(n)
 })
}
function select(idValue,node){
 selected=idValue;board.querySelectorAll(".block.selected").forEach(function(n){n.classList.remove("selected")});
 if(node)node.classList.add("selected");renderInspector()
}
function moveStart(ev,e,node){
 if(ev.button!==0||ev.target.closest(".resize-handle"))return;ev.preventDefault();select(e.id,node);checkpoint();
 var sx=ev.clientX,sy=ev.clientY,ox=e.x,oy=e.y;node.setPointerCapture(ev.pointerId);
 function move(m){e.x=clamp(snap(ox+(m.clientX-sx)/zoom),0,W-e.w);e.y=clamp(snap(oy+(m.clientY-sy)/zoom),0,H-e.h);node.style.left=e.x+"px";node.style.top=e.y+"px";coords(e)}
 function end(m){node.releasePointerCapture(m.pointerId);node.removeEventListener("pointermove",move);node.removeEventListener("pointerup",end);node.removeEventListener("pointercancel",end);save()}
 node.addEventListener("pointermove",move);node.addEventListener("pointerup",end);node.addEventListener("pointercancel",end)
}
function resizeStart(ev,e,node){
 if(ev.button!==0)return;ev.preventDefault();ev.stopPropagation();select(e.id,node);checkpoint();
 var handle=ev.currentTarget,sx=ev.clientX,sy=ev.clientY,ow=e.w,oh=e.h;handle.setPointerCapture(ev.pointerId);
 function move(m){e.w=clamp(snap(ow+(m.clientX-sx)/zoom),80,W-e.x);e.h=clamp(snap(oh+(m.clientY-sy)/zoom),50,H-e.y);node.style.width=e.w+"px";node.style.height=e.h+"px";coords(e)}
 function end(m){handle.releasePointerCapture(m.pointerId);handle.removeEventListener("pointermove",move);handle.removeEventListener("pointerup",end);handle.removeEventListener("pointercancel",end);save()}
 handle.addEventListener("pointermove",move);handle.addEventListener("pointerup",end);handle.addEventListener("pointercancel",end)
}
function renderInspector(){
 var e=chosen();$("emptyInspector").hidden=!!e;$("inspectorForm").hidden=!e;$("selectionKind").textContent=e?e.type:"Aucun";
 $("duplicateBtn").disabled=!e;$("deleteBtn").disabled=!e;if(!e)return;
 $("propType").value=e.type;$("propTitle").value=e.title;$("propBody").value=e.body;$("propInstruction").value=e.instruction;$("propTags").value=(e.tags||[]).join(", ");coords(e)
}
function coords(e){$("propX").value=Math.round(e.x);$("propY").value=Math.round(e.y);$("propW").value=Math.round(e.w);$("propH").value=Math.round(e.h)}
function renderZoom(){
 stage.style.transform="scale("+zoom+")";stage.style.marginRight=Math.round(-W*(1-zoom))+"px";stage.style.marginBottom=Math.round(-H*(1-zoom))+"px";
 stage.classList.toggle("grid",$("gridToggle").checked);$("zoomRange").value=Math.round(zoom*100);$("zoomValue").textContent=Math.round(zoom*100)+"%"
}
function add(type){
 checkpoint();var e=block(type,type==="note"?"Nouvelle annotation":"Nouveau bloc",type==="note"?"Écris ici ce que tu veux changer.":"Contenu à personnaliser.",380,180,type==="note"?300:360,type==="note"?150:190,type==="note"?["annotation"]:["nouveau"]);
 e.z=maxZ()+1;page().elements.push(e);selected=e.id;renderBoard();renderInspector();save();if(type==="note")$("propBody").focus()
}
function duplicate(){
 var e=chosen();if(!e)return;checkpoint();var n=copy(e);n.id=id(e.type);n.title+=" — copie";n.x=clamp(e.x+30,0,W-e.w);n.y=clamp(e.y+30,0,H-e.h);n.z=maxZ()+1;page().elements.push(n);selected=n.id;renderBoard();renderInspector();save()
}
function remove(){
 if(!chosen())return;checkpoint();page().elements=page().elements.filter(function(e){return e.id!==selected});selected=null;renderBoard();renderInspector();save()
}
function bind(fieldId,prop,parser){
 var f=$(fieldId);
 f.onfocus=function(){if(!editMark){checkpoint();editMark=true}};
 f.onblur=function(){editMark=false};
 f.oninput=function(){
  var e=chosen();if(!e)return;e[prop]=parser?parser(f.value,e):f.value;
  if(prop==="x")e.x=clamp(e.x,0,W-e.w);if(prop==="y")e.y=clamp(e.y,0,H-e.h);if(prop==="w")e.w=clamp(e.w,80,W-e.x);if(prop==="h")e.h=clamp(e.h,50,H-e.y);
  renderBoard();save()
 }
}
function brief(){
 var lines=["CYENGINEHUB — BRIEF DE MAQUETTE","Exporté le "+new Date().toLocaleString("fr-FR"),""],found=false;
 state.pages.forEach(function(p){var marked=p.elements.filter(function(e){return e.type==="note"||e.instruction.trim()});if(!(p.comment||"").trim()&&!marked.length)return;found=true;lines.push("## "+p.name);if((p.comment||"").trim())lines.push("Note générale : "+p.comment.trim());marked.forEach(function(e){lines.push("- "+e.title+" ["+e.type+"] — position "+e.x+","+e.y+", taille "+e.w+"×"+e.h);if(e.type==="note"&&e.body.trim())lines.push("  Annotation : "+e.body.trim());if(e.instruction.trim())lines.push("  Instruction : "+e.instruction.trim())});lines.push("")});
 if(!found)lines.push("Aucune annotation ni instruction.");return lines.join("\n")
}
function legacyCopy(text){var a=document.createElement("textarea");a.value=text;a.style.cssText="position:fixed;opacity:0";document.body.appendChild(a);a.select();document.execCommand("copy");a.remove();toast("Brief copié")}
function copyBrief(){var text=brief();if(navigator.clipboard&&navigator.clipboard.writeText)navigator.clipboard.writeText(text).then(function(){toast("Brief copié")},function(){legacyCopy(text)});else legacyCopy(text)}
function exportFile(){save();var blob=new Blob([JSON.stringify(state,null,2)],{type:"application/json"}),a=document.createElement("a");a.href=URL.createObjectURL(blob);a.download="cyenginehub-layout-"+new Date().toISOString().slice(0,10)+".json";a.click();setTimeout(function(){URL.revokeObjectURL(a.href)},1000);toast("Maquette exportée")}

$("undoBtn").onclick=undo;$("redoBtn").onclick=redo;$("addBlockBtn").onclick=function(){add("panel")};$("addNoteBtn").onclick=function(){add("note")};
$("duplicateBtn").onclick=duplicate;$("deleteBtn").onclick=remove;$("copyBriefBtn").onclick=copyBrief;$("exportBtn").onclick=exportFile;
$("importBtn").onclick=function(){$("importInput").click()};
$("importInput").onchange=function(ev){
 var f=ev.target.files[0];if(!f)return;var r=new FileReader();
 r.onload=function(){try{var incoming=JSON.parse(r.result);if(!incoming||!Array.isArray(incoming.pages))throw Error();checkpoint();state=incoming;active=state.activePageId||state.pages[0].id;selected=null;render();save();toast("Maquette importée")}catch(e){toast("Fichier JSON invalide")}};
 r.readAsText(f);ev.target.value=""
};
$("zoomRange").oninput=function(){zoom=Number(this.value)/100;renderZoom();save()};
$("gridToggle").checked=state.grid!==false;$("gridToggle").onchange=function(){renderZoom();save()};
$("addPageBtn").onclick=function(){
 var name=prompt("Nom de la nouvelle page :","Nouvelle page");if(!name)return;checkpoint();var pid=id("page");
 state.pages.push({id:pid,name:name.trim(),description:"Page personnalisée",comment:"",elements:shell(name.trim(),"Page libre à organiser et annoter.")});
 active=pid;selected=null;render();save()
};
$("renamePageBtn").onclick=function(){var name=prompt("Nouveau nom :",page().name);if(!name||name.trim()===page().name)return;checkpoint();page().name=name.trim();render();save()};
$("duplicatePageBtn").onclick=function(){checkpoint();var p=copy(page());p.id=id("page");p.name+=" — copie";p.elements.forEach(function(e){e.id=id(e.type)});state.pages.push(p);active=p.id;selected=null;render();save()};
$("resetPageBtn").onclick=function(){
 var source=pristine.find(function(p){return p.id===active});if(!source){toast("Cette page personnalisée n’a pas de modèle");return}
 if(!confirm("Réinitialiser cette page et effacer ses annotations ?"))return;checkpoint();state.pages[state.pages.findIndex(function(p){return p.id===active})]=copy(source);selected=null;render();save()
};
$("pageComment").onfocus=function(){if(!editMark){checkpoint();editMark=true}};$("pageComment").onblur=function(){editMark=false};$("pageComment").oninput=function(){page().comment=this.value;save()};
bind("propType","type");bind("propTitle","title");bind("propBody","body");bind("propInstruction","instruction");
bind("propTags","tags",function(v){return v.split(",").map(function(x){return x.trim()}).filter(Boolean)});
bind("propX","x",function(v,e){return Number.isFinite(Number(v))?Number(v):e.x});bind("propY","y",function(v,e){return Number.isFinite(Number(v))?Number(v):e.y});
bind("propW","w",function(v,e){return Number.isFinite(Number(v))?Number(v):e.w});bind("propH","h",function(v,e){return Number.isFinite(Number(v))?Number(v):e.h});
$("frontBtn").onclick=function(){var e=chosen();if(!e)return;checkpoint();e.z=maxZ()+1;renderBoard();save()};
$("backBtn").onclick=function(){var e=chosen();if(!e)return;checkpoint();e.z=minZ()-1;renderBoard();save()};
document.onkeydown=function(ev){
 var tag=document.activeElement&&document.activeElement.tagName,editing=tag==="INPUT"||tag==="TEXTAREA"||tag==="SELECT";
 if(ev.ctrlKey&&ev.key.toLowerCase()==="z"){ev.preventDefault();ev.shiftKey?redo():undo();return}
 if(editing)return;
 if(ev.ctrlKey&&ev.key.toLowerCase()==="d"){ev.preventDefault();duplicate();return}
 if((ev.key==="Delete"||ev.key==="Backspace")&&chosen()){ev.preventDefault();remove();return}
 var dirs={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]},d=dirs[ev.key];
 if(d&&chosen()){ev.preventDefault();checkpoint();var e=chosen(),step=ev.shiftKey?10:1;e.x=clamp(e.x+d[0]*step,0,W-e.w);e.y=clamp(e.y+d[1]*step,0,H-e.h);renderBoard();renderInspector();save()}
};
render();save();
})();
