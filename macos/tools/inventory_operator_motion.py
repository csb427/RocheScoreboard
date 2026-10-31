#!/usr/bin/env python3
"""Source coverage ledger for the operator motion rollout (no app mutation).

Enumerates declarations, template controls, dynamic creation/factory sites and
presentation regions. A declaration is not proof of runtime behavior. Reviews
and evidence are explicit entries in decisions.json, never inferred from a style.
"""
from __future__ import annotations

import argparse
import bisect
import csv
import hashlib
import json
import re
from collections import Counter
from pathlib import Path
import xml.etree.ElementTree as ET
import xml.parsers.expat

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'Roche_Scoreboard'
DEST = ROOT / 'docs/refinement/apple-motion-rollout'
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
CONTROLS = set('Button RepeatButton ToggleButton RadioButton CheckBox TextBox PasswordBox ComboBox ComboBoxItem ListBox ListBoxItem ListView ListViewItem TreeView TreeViewItem DataGrid DataGridTextColumn DataGridCheckBoxColumn Slider ScrollBar ScrollViewer ProgressBar Expander TabControl TabItem Menu MenuItem ContextMenu Popup DatePicker Calendar Thumb OperatorActionBorder OperatorSearchBox OperatorSegmentedPanel OperatorMaterialPreferences OperatorSymbol'.split())
VISUAL_SITES={'Border','OperatorGlassSurface'}
FACTORIES = 'WorkflowButton|ChooseAsync|FormAsync|CorrectionFormAsync|ShowErrorAsync|AskAsync|ShowAsync|ShowDialog|MessageBox.Show|CricketPrompt.ChooseAsync|CricketPrompt.ShowAsync|CricketPrompt.ShowCorrectionAsync'
CREATE = re.compile(r'\bnew\s+(?:[\w.]+\.)?('+'|'.join(sorted(CONTROLS | VISUAL_SITES | {'Window','OpenFileDialog','SaveFileDialog','FolderBrowserDialog','ColorDialog'}, key=len, reverse=True))+r')\s*(?:[({])')
TYPED = re.compile(r'\b('+'|'.join(sorted(CONTROLS, key=len, reverse=True))+r')\s+(\w+)\s*=\s*new\s*\(')
CALLS = re.compile(r'(?<![\w])('+FACTORIES+r')\s*\(')
METHOD = re.compile(r'^\s*(?:public|private|internal|protected)\s+(?:(?:static|async|override|virtual|sealed|partial)\s+)*(?:[\w<>?.,\[\]]+\s+)+(\w+)\s*\(',re.MULTILINE)
NATIVE_GAP = 'Native Windows WPF pointer/keyboard/focus, scaling, compositor and assistive-technology acceptance not run here; Mac desktop/hardware checks separate.'


def domain(path: str) -> str:
    lower = path.lower()
    if 'presentationgallery' in lower:return 'utilities'
    if any(word in lower for word in ['scorebug','summarycontrol','scorewormcontrol','statsscreen','screensaver','breakscreencontrol','introscreen','calibration','weatherscreen','emergencyvideo','overlayhost','chartvisual']):
        return 'presentation/output'
    if 'cricketsetup' in lower or 'setupwizard' in lower: return 'setup'
    if 'cricket' in lower: return 'cricket'
    if 'training' in lower: return 'training'
    if 'settings' in lower or 'preferences' in lower: return 'settings'
    if any(word in lower for word in ['colorpicker','logocrop','teamlibrary','clubpreset']): return 'clubs/assets'
    if 'resume' in lower or 'recovery' in lower or 'scoreevent' in lower: return 'history/recovery'
    if 'video' in lower or 'media' in lower: return 'media'
    if 'sportselection' in lower or 'homecommands' in lower or 'unifiedsetup' in lower: return 'home/navigation'
    if 'mainwindow' in lower or 'controlpanel' in lower: return 'afl/operator'
    if '/resources/' in lower or '/designsystem/' in lower or '/services/operator' in lower: return 'shared primitives'
    return 'utilities'


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def declaration_digest(row:dict) -> str:
    return hashlib.sha256(json.dumps(row['current_source'],sort_keys=True).encode()).hexdigest()


def code_only(raw: str) -> str:
    """Mask comments/literals without changing offsets or source line numbers.

    This is a lexical navigation aid, not a C# semantic compiler. Dynamic routes
    and interpolated/raw-language edge cases still need explicit source review.
    """
    pattern = r'//[^\r\n]*|/\*[\s\S]*?\*/|@?"(?:""|\\[\s\S]|[^"\\])*"|\'(?:\\.|[^\'\\])*\''
    return re.sub(pattern, lambda match: ''.join('\n' if char == '\n' else ' ' for char in match[0]), raw)


def additional_typed_sites(code: str, ui_types: set[str]):
    """Find target-typed declarations omitted by the original control-only pass.

    Initializers are split only at outer commas/semicolons, so object members,
    nested calls and multiple declarators keep their actual declared UI type.
    Legacy sites stay in their original pass to preserve existing ordinal IDs.
    """
    names = '|'.join(sorted(map(re.escape, ui_types), key=len, reverse=True))
    accessors = r'\{(?:\s*(?:(?:public|private|protected|internal)\s+)*(?:get|set|init)\s*;\s*)+\}'
    declarations = re.compile(r'\b(' + names + r')\??\s+(@?\w+)\s*(' + accessors + r')?\s*=')
    legacy_ends = {match.end() for match in TYPED.finditer(code)}
    emitted = set()
    for declaration in declarations.finditer(code):
        kind, variable = declaration[1], declaration[2]
        offset, start = declaration.end(), declaration.start()
        prefix = code[code.rfind('\n', 0, start) + 1:start]
        field = bool(declaration[3] or re.search(r'\b(?:private|protected|internal|public)\b[^()]*$', prefix))
        while offset < len(code):
            initializer = re.match(r'\s*new\s*\(', code[offset:])
            if initializer:
                end = offset + initializer.end()
                if end not in legacy_ends and end not in emitted:
                    emitted.add(end)
                    yield start, kind, variable, field
            stack = []
            while offset < len(code):
                char = code[offset]
                if char in '([{':
                    stack.append(char)
                elif char in ')]}':
                    if not stack:
                        break
                    stack.pop()
                elif char in ',;' and not stack:
                    break
                offset += 1
            if offset >= len(code) or code[offset] != ',':
                break
            following = re.match(r'\s*(@?\w+)\s*=', code[offset + 1:])
            if not following:
                break
            start = offset + 1 + following.start(1)
            variable = following[1]
            offset += 1 + following.end()


def scan() -> tuple[list[dict], list[dict]]:
    entries, files = [], []
    custom_types=set()
    for path in SRC.rglob('*.cs'):
        if {'obj','bin'}.intersection(path.parts):continue
        custom_types.update(re.findall(r'class\s+(\w+)\s*:\s*(?:Window|UserControl|FrameworkElement|ContentControl|Border|Panel|ScrollViewer)\b',code_only(path.read_text(encoding='utf-8-sig'))))
    for path in SRC.rglob('*.xaml'):
        cls=ET.parse(path).getroot().get(X+'Class')
        if cls:custom_types.add(cls.rsplit('.',1)[-1])
    ui_types = CONTROLS | VISUAL_SITES | custom_types | {'Window','OpenFileDialog','SaveFileDialog','FolderBrowserDialog','ColorDialog'}
    create=re.compile(r'\bnew\s+(?:[\w.]+\.)?('+'|'.join(sorted(ui_types,key=len,reverse=True))+r')\s*(?:[({])')
    for path in sorted(SRC.rglob('*.xaml')):
        relative = str(path.relative_to(ROOT)); raw = path.read_text(encoding='utf-8-sig')
        root = ET.fromstring(raw); counts = Counter(); feature = str(path.relative_to(SRC));parents={child:parent for parent in root.iter() for child in parent}
        files.append({'source':relative,'sha256':digest(path),'kind':'xaml','domain':domain(relative),'feature':feature})
        entries.append({'id':feature+'::surface','feature':feature,'domain':domain(relative),'source':relative,'line':1,'kind':'surface','type':root.tag.split('}')[-1],'name':root.get(X+'Class',''),'label':'','current_source':{'root_attributes':dict(root.attrib)}})
        tag_lines=[];parser=xml.parsers.expat.ParserCreate();parser.StartElementHandler=lambda *_:tag_lines.append(parser.CurrentLineNumber);parser.Parse(raw,True)
        assert len(tag_lines)==len(list(root.iter()))
        for index,element in enumerate(root.iter()):
            kind = element.tag.split('}')[-1]; name=element.get(X+'Name',''); attrs=dict(element.attrib)
            region=bool(name and re.search(r'(Panel|Page|Tab|Overlay|Container|Card|Drawer|Section|Screen|Preview|Host|Step)$',name))
            numeric=kind=='TextBlock' and bool(name and re.search(r'Score|Runs|Wickets|Clock|Timer|Overs|Rate|Economy',name))
            event_input=any(re.search(r'^(?:Preview)?(?:Mouse|KeyDown|KeyUp|Touch|Stylus)|^Click$|^(?:Drag|Drop)',key.split('}')[-1]) for key in attrs) or element.get('Focusable')=='True' or element.get('Cursor')=='Hand'
            if kind not in CONTROLS|custom_types and not region and not numeric and not event_input: continue
            key=name or element.get('Tag') or element.get('Click') or element.get('Header') or element.get('Content') or kind
            if not name:
                ancestor=parents.get(element);anchor='root'
                while ancestor is not None:
                    anchor=ancestor.get(X+'Name',ancestor.get(X+'Key',''))
                    if anchor:break
                    ancestor=parents.get(ancestor)
                key='@'+(anchor or 'root')+':'+key+(':'+element.get('Kind') if element.get('Kind') else '')
            counts[(kind,key)]+=1; identity=f'{feature}::{kind}:{key}:{counts[(kind,key)]}'
            ancestor=element;scope=''
            while ancestor is not None:
                region_name=ancestor.get(X+'Name','')
                if region_name and re.search(r'(Tab|Step|Overlay|Panel|Drawer|Section|Card)$',region_name):scope=region_name;break
                ancestor=parents.get(ancestor)
            context=' '.join([relative,name,scope,element.get('ToolTip','')]).lower()
            area=domain(relative)
            if path.name=='MainWindow.xaml':
                if any(word in context for word in ['video','media','volume']):area='media'
                elif any(word in context for word in ['settings','preferences']):area='settings'
                elif any(word in context for word in ['history','scorelog','resume','recovery']):area='history/recovery'
                elif any(word in context for word in ['command','sportselection']):area='home/navigation'
            # A source line is a navigation aid; identity does not depend on it.
            line=tag_lines[index]
            label=element.get('Content',element.get('Header',element.get('Text','')))
            if not label:label=' · '.join(child.get('Text','') for child in element.iter() if child.tag.endswith('}TextBlock') and child.get('Text'))[:220]
            entries.append({'id':identity,'feature':feature+('::'+scope if scope else ''),'domain':area,'source':relative,'line':line,'kind':'interactive' if kind in CONTROLS or event_input else 'numeric' if numeric else 'custom visual' if kind in custom_types else 'region','type':kind,'name':name,'label':label,'current_source':attrs})
    for path in sorted(SRC.rglob('*.cs')):
        if {'obj','bin'}.intersection(path.parts) or re.search(r'Qa\.cs$|Checks\.cs$|Scenarios\.cs$',path.name): continue
        raw=path.read_text(encoding='utf-8-sig'); relative=str(path.relative_to(ROOT)); lines=raw.splitlines(); method='initialization'; counts=Counter(); found=[];return_control=None
        code=code_only(raw);code_lines=code.splitlines()
        line_starts=[0]+[m.end() for m in re.finditer('\n',code)]
        factory_decl=re.compile(r'\b(?:'+'|'.join(sorted(CONTROLS|VISUAL_SITES|custom_types,key=len,reverse=True))+r')\??\s+(\w+)\s*\(')
        definitions=list(factory_decl.finditer(code))
        keywords={'if','for','foreach','while','switch','catch','using','lock','return','new','nameof','typeof'}
        local_factories={m[1] for m in definitions if m[1] not in keywords}
        local_calls=re.compile(r'(?<![\w.])('+'|'.join(sorted(map(re.escape,local_factories),key=len,reverse=True))+r')\s*\(') if local_factories else None
        sites_by_line={}
        sites=list(create.finditer(code))+list(TYPED.finditer(code))+list(CALLS.finditer(code))
        if local_calls:
            existing={(m.start(),m[1]) for m in sites}
            sites += [m for m in local_calls.finditer(code) if (m.start(),m[1]) not in existing
                      and not any(d.start()<=m.start()<d.end() for d in definitions)]
        for site in sorted(sites,key=lambda match:match.start()):
            sites_by_line.setdefault(bisect.bisect_right(line_starts,site.start()),[]).append(site)
        contexts = {}
        extra_returns = []
        extended_return = None
        for number,line in enumerate(code_lines,1):
            declaration=METHOD.search(line)
            if declaration:
                method=declaration[1]
                return_control=next((kind for kind in CONTROLS if re.search(r'\b'+kind+r'\??\s+'+re.escape(method)+r'\s*\(',line)),None)
                extended_return=next((kind for kind in ui_types if re.search(r'\b'+re.escape(kind)+r'\??\s+'+re.escape(method)+r'\s*\(',line)),None)
            contexts[number] = method
            sites=[match for match in sites_by_line.get(number,[]) if not declaration or match[1]!=method]
            for match in sites:
                kind=match[1];counts[(method,kind)]+=1; feature=str(path.relative_to(SRC))+'::'+method
                identity=f'{feature}::{kind}:{counts[(method,kind)]}'
                snippet='\n'.join(lines[max(0,number-2):min(len(lines),number+3)])
                found.append({'id':identity,'feature':feature,'domain':domain(relative+'::'+method),'source':relative,'line':number,'kind':'dynamic visual' if kind in VISUAL_SITES else 'dynamic/factory','type':kind,'name':method,'label':'','current_source':{'snippet':snippet}})
            if return_control and re.search(r'(?:return|=>)\s+new\s*\(',line):
                kind=return_control;counts[(method,kind)]+=1;feature=str(path.relative_to(SRC))+'::'+method
                found.append({'id':f'{feature}::{kind}:{counts[(method,kind)]}','feature':feature,'domain':domain(relative),'source':relative,'line':number,'kind':'implicit factory','type':kind,'name':method,'label':'','current_source':{'snippet':'\n'.join(lines[max(0,number-2):min(len(lines),number+3)])}})
            elif extended_return and re.search(r'(?:\breturn\s+|=>\s*)new\s*\(', line):
                extra_returns.append((number, method, extended_return))
        # Allocate additional sites after every original site, preserving all
        # existing ordinal IDs even when a newly recognised field occurs first.
        for offset, kind, variable, field in additional_typed_sites(code, ui_types):
            number = bisect.bisect_right(line_starts, offset)
            owner = 'initialization' if field else contexts[number]
            counts[(owner,kind)] += 1
            feature = str(path.relative_to(SRC)) + '::' + owner
            found.append({'id':f'{feature}::{kind}:{counts[(owner,kind)]}','feature':feature,'domain':domain(relative+'::'+owner),'source':relative,'line':number,'kind':'dynamic visual' if kind in VISUAL_SITES else 'dynamic/factory','type':kind,'name':variable,'label':'','current_source':{'snippet':'\n'.join(lines[max(0,number-2):min(len(lines),number+3)]),'declarator':variable,'scope':'field/property initializer' if field else 'local initializer'}})
        for number, owner, kind in extra_returns:
            counts[(owner,kind)] += 1
            feature = str(path.relative_to(SRC)) + '::' + owner
            found.append({'id':f'{feature}::{kind}:{counts[(owner,kind)]}','feature':feature,'domain':domain(relative),'source':relative,'line':number,'kind':'implicit factory','type':kind,'name':owner,'label':'','current_source':{'snippet':'\n'.join(lines[max(0,number-2):min(len(lines),number+3)]),'scope':'return expression'}})
        surfaces=[definition for definition in re.finditer(r'class\s+(\w+)\s*:\s*(Window|UserControl|FrameworkElement|ContentControl|Border|Panel|ScrollViewer)\b',code)
                  if not (path.parent/(definition[1]+'.xaml')).exists()]
        for definition in surfaces:
            feature=str(path.relative_to(SRC))+'::surface'+(':'+definition[1] if len(surfaces)>1 else '')
            found.append({'id':feature,'feature':feature,'domain':domain(relative),'source':relative,'line':raw.count('\n',0,definition.start())+1,'kind':'code surface','type':definition[2],'name':definition[1],'label':'','current_source':{'class':definition[1],'base':definition[2]}})
        if path.parent.name=='Services' and (path.stem.endswith('Motion') or path.stem in {'OperatorDesign','OperatorFeedback','OperatorInputFocus','OperatorRoundedClip','ThemeManager','ScrollActivity'}):
            feature=str(path.relative_to(SRC))+'::interaction-infrastructure'
            found.append({'id':feature,'feature':feature,'domain':'presentation/output' if path.stem=='BroadcastMotion' else 'shared primitives','source':relative,'line':1,'kind':'interaction infrastructure','type':'motion/input service','name':path.stem,'label':'','current_source':{'source_sha256':digest(path)}})
        if path.parent.name=='Views' or path.name.startswith('MainWindow.') or path.name=='App.xaml.cs':
            feature=str(path.relative_to(SRC))+'::controller'
            found.append({'id':feature,'feature':feature,'domain':domain(relative),'source':relative,'line':1,'kind':'controller','type':'view controller','name':path.name,'label':'','current_source':{'source_sha256':digest(path),'methods':[m[1] for m in METHOD.finditer(code)]}})
        if found or re.search(r':\s*(?:Window|UserControl)\b',raw):
            files.append({'source':relative,'sha256':digest(path),'kind':'code','domain':domain(relative),'features':sorted({entry['feature'] for entry in found})})
        entries+=found
    return entries,files


def build() -> dict:
    entries,files=scan()
    decisions_path=DEST/'decisions.json'
    decisions=json.loads(decisions_path.read_text()) if decisions_path.exists() else {'entries':{},'features':{}}
    for row in entries:
        review=decisions.get('entries',{}).get(row['id'])
        feature=decisions.get('features',{}).get(row['feature'])
        row.update({'current_behavior':'Source declaration recorded; runtime observation pending.','intended_treatment':'Pending individual review.','rationale':'Do not infer coverage from shared styles.','implementation_state':'unreviewed','verification':[],'native_gap':NATIVE_GAP})
        if feature:
            row['feature_review']=feature
        if review:
            row.update(review)
            row['declaration_changed_since_review']=review.get('reviewed_declaration_sha256') not in (None,declaration_digest(row))
            if row['declaration_changed_since_review']:row['implementation_state']='needs_recheck'
        row['declaration_sha256']=declaration_digest(row)
    return {'schema':1,'scope':'Authoritative Windows declarations, dynamic creation/factory sites, numeric presentation fields and view regions; templates are definitions, not additional runtime instances. Source scanners are navigation aids; manual route reviews must resolve dynamic/implicit construction not expressible by static scanning.','completion_rule':'Every entry reviewed with an explicit treatment or justified retention; implemented changes verified. Native platform gaps remain explicit and do not count as desktop acceptance.','files':files,'entries':entries,'counts':{'source_files':len(files),'xaml_files':sum(f['kind']=='xaml' for f in files),'entries':len(entries),'by_domain':dict(sorted(Counter(row['domain'] for row in entries).items())),'by_state':dict(sorted(Counter(row['implementation_state'] for row in entries).items()))}}


def write(data:dict) -> None:
    DEST.mkdir(parents=True,exist_ok=True)
    (DEST/'coverage.json').write_text(json.dumps(data,indent=2,ensure_ascii=False)+'\n')
    with (DEST/'controls.csv').open('w',newline='') as stream:
        columns=['id','feature','domain','source','line','kind','type','name','label','current_behavior','intended_treatment','rationale','implementation_state','verification','native_gap']
        writer=csv.DictWriter(stream,columns,extrasaction='ignore');writer.writeheader()
        for row in data['entries']:
            writer.writerow({**row,'verification':'; '.join(row['verification'])})
    features={}
    for row in data['entries']:features.setdefault(row['feature'],[]).append(row)
    text=['# App-wide motion coverage','', 'Source inventory, not a claim of visual or runtime acceptance. See `NEXT.md` for the active entry and `decisions.json` for authored per-entry decisions.','',f"{len(features)} source feature groups; {data['counts']['source_files']} files; {data['counts']['entries']} control/region/factory entries.",'','| Feature | Area | Entries | Reviewed |','| --- | --- | ---: | ---: |']
    for feature,rows in features.items():text.append(f"| {feature.replace('|','/')} | {rows[0]['domain']} | {len(rows)} | {sum(row['implementation_state']!='unreviewed' for row in rows)} |")
    (DEST/'COVERAGE.md').write_text('\n'.join(text)+'\n')


def main() -> None:
    parser=argparse.ArgumentParser();parser.add_argument('--check',action='store_true');args=parser.parse_args();data=build()
    if args.check:
        saved=json.loads((DEST/'coverage.json').read_text())
        if saved!=data:raise SystemExit('Motion inventory is stale; regenerate after source or decision changes.')
    else:write(data)
    print(json.dumps(data['counts'],indent=2))


if __name__=='__main__':main()
