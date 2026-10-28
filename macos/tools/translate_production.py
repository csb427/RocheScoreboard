#!/usr/bin/env python3
"""Repeatable, source-tracked native translations of production views.

Generated C# retains the production handlers and presentation rules. Platform
mapping is explicit here; native adapters live in Native/. Never edit generated
files or use this inventory as evidence of completed runtime parity.
"""
from __future__ import annotations
import hashlib
import json
import math
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'Roche_Scoreboard'
OUT = ROOT / 'macos/RocheScoreboard.Mac/Native/Generated'
WPF = 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'
AV = 'https://github.com/avaloniaui'
X = 'http://schemas.microsoft.com/winfx/2006/xaml'
PORT = 'clr-namespace:RocheScoreboard.Mac.Production'
ET.register_namespace('', AV)
ET.register_namespace('x', X)
ET.register_namespace('port', PORT)

VIEWS = ['SportSelectionScreen', 'ScorebugControl', 'CalibrationOverlay', 'BreakScreenControl', 'IntroScreenControl', 'PresentationScoreBar', 'QuarterBreakScorebugControl', 'ScorewormControl', 'StatsScreenControl', 'TrainingScorebugControl', 'CricketScorebugControl', 'CricketSummaryControl', 'WeatherScreenControl', 'MatchLoadingScreen', 'ColorPickerWindow', 'LogoCropWindow', 'TeamLibraryWindow', 'SetupWizard', 'TrainingControlPanel', 'CricketControlPanel', 'CricketSetupWizard', 'GlobalSettingsScreen', 'ScoreEventEditorWindow', 'ResumeGameDialog', 'CloudRemoteLauncher', 'ControlPanel', 'VideoPlayerControl', 'OutputScreensaverControl', 'UpdatePromptWindow', 'AflPresentationGalleryWindow', 'DesignSystemGalleryWindow', 'CricketDeliveryDialog', 'ResumeMatchPrompt', 'MainWindow']
EVENTS = {
    'MouseLeftButtonUp': 'PointerReleased', 'MouseLeftButtonDown': 'PointerPressed',
    'MouseEnter': 'PointerEntered', 'MouseLeave': 'PointerExited',
    'MouseRightButtonUp': 'PointerReleased', 'MouseRightButtonDown': 'PointerPressed',
    'LostMouseCapture': 'PointerCaptureLost', 'LostKeyboardFocus': 'LostFocus',
    'SelectionChanged': 'SelectionChanged', 'TextChanged': 'TextChanged', 'Checked': 'IsCheckedChanged', 'Unchecked': 'IsCheckedChanged',
    'MouseMove': 'PointerMoved', 'PreviewMouseWheel': 'PointerWheelChanged',
    'PreviewTextInput': 'TextInput', 'PreviewKeyDown': 'KeyDown', 'PreviewMouseLeftButtonDown': 'PointerPressed', 'PreviewMouseLeftButtonUp': 'PointerReleased',
}
OBJECTS = {'LinearGradientBrush', 'RadialGradientBrush', 'SolidColorBrush', 'GradientStop',
           'ColumnDefinition', 'RowDefinition', 'TranslateTransform', 'ScaleTransform',
           'SkewTransform', 'RotateTransform', 'TransformGroup', 'DropShadowEffect', 'BlurEffect'}

def relative(v):
    return ','.join(f'{float(n) * 100:g}%' for n in v.split(','))

def translate_markup(name):
    source = SRC / f'{name}.xaml' if '/' in name or name == 'MainWindow' else SRC / 'Views' / f'{name}.xaml'
    namespace_aliases = dict(value for event, value in ET.iterparse(source, events=['start-ns']))
    root = ET.fromstring(source.read_bytes())
    for prefix, uri in namespace_aliases.items():
        if prefix and uri.startswith('clr-namespace:Roche_'): root.set('xmlns:' + prefix, uri)
    if not name.startswith(('DesignSystem/', 'Resources/')):
        root.set(f'{{{PORT}}}ProductionNames.Root', 'True')
    root.set('xmlns:native', 'clr-namespace:RocheScoreboard.Mac.Native')
    root.set('xmlns:motion', 'clr-namespace:Roche_Scoreboard.Services')
    root.set('xmlns:services', 'clr-namespace:Roche_Scoreboard.Services')
    root.set('xmlns:glass', 'clr-namespace:Roche_Scoreboard.Views')
    root.set('xmlns:conv', 'clr-namespace:Avalonia.Data.Converters;assembly=Avalonia.Base')
    root.set('xmlns:fonts', 'clr-namespace:RocheScoreboard.Mac.Native')
    names = []
    events = []
    wrappers = []
    changes = []
    # Layout scaling and render translation must remain independent: long
    # classic team names use both simultaneously. Avalonia's layout transform
    # owns a small wrapper Grid so it cannot replace the text's marquee.
    for parent in list(root.iter()):
        for child in list(parent):
            layout = next((p for p in child if p.tag.endswith('}TextBlock.LayoutTransform')), None)
            if layout is None:
                continue
            index = list(parent).index(child)
            wrapper = ET.Element(f'{{{WPF}}}LayoutTransformControl', {'ClipToBounds': 'False'})
            for key in list(child.attrib):
                if key.startswith('Grid.') or key in {'Margin', 'HorizontalAlignment', 'VerticalAlignment'}:
                    wrapper.set(key, child.attrib.pop(key))
            child.remove(layout)
            layout.tag = f'{{{WPF}}}LayoutTransformControl.LayoutTransform'
            wrapper.append(layout)
            container = ET.SubElement(wrapper, f'{{{WPF}}}Grid')
            container.append(child)
            parent.remove(child)
            parent.insert(index, wrapper)
            changes.append({'name': child.get(f'{{{X}}}Name'), 'property': 'LayoutTransform', 'translation': 'Independent native layout wrapper, retaining render marquee'})
    # Convert WPF template triggers to native pseudo-class styles on their owner.
    parents = {c: p for p in root.iter() for c in p}
    for node in list(root.iter()):
        if node.tag.endswith('.Triggers') and node.tag not in {f'{{{WPF}}}ControlTemplate.Triggers', f'{{{WPF}}}Style.Triggers'}:
            scenes = []
            for trigger in node:
                if not trigger.tag.endswith('}EventTrigger'):
                    raise ValueError(f'{name}: scene trigger needs an explicit native mapping')
                animations = []
                board = next(e for e in trigger.iter() if e.tag.endswith('}Storyboard'))
                for animation in board:
                    if not animation.tag.endswith('}DoubleAnimation'):
                        raise ValueError('Scene timeline type requires native implementation')
                    spec = dict(animation.attrib)
                    spec['_StoryboardRepeat'] = board.get('RepeatBehavior', '1x')
                    for easing in animation.iter():
                        if easing.tag.rsplit('}', 1)[-1].endswith('Ease'):
                            spec['Easing'] = easing.tag.rsplit('}', 1)[-1]
                            spec.update(easing.attrib)
                    animations.append(spec)
                scenes.append({'event': trigger.get('RoutedEvent'), 'animations': animations})
            parents[node].set('{clr-namespace:RocheScoreboard.Mac.Native}SceneMotion.Spec', json.dumps(scenes, separators=(',', ':')))
            parents[node].remove(node)
    for node in list(root.iter()):
        if node.tag not in {f'{{{WPF}}}ControlTemplate.Triggers', f'{{{WPF}}}Style.Triggers'}:
            continue
        template = parents[node]
        prop = parents[template]
        owner = template if node.tag.endswith('}Style.Triggers') else parents[prop]
        if node.tag.endswith('}ControlTemplate.Triggers') and prop.tag.endswith('Setter.Value'):
            owner = parents[owner]  # Setter -> Style (native ControlTheme)
        owner_type = owner.get('TargetType') or owner.tag.rsplit('}', 1)[-1]
        is_theme = owner.tag.endswith('}Style')
        property_node = owner if is_theme else ET.SubElement(owner, f'{{{WPF}}}{owner_type}.Styles')
        event_specs = []
        for trigger in node:
            kind = trigger.tag.rsplit('}', 1)[-1]
            if kind == 'EventTrigger':
                animations = []
                for animation in trigger.iter():
                    if animation.tag.endswith('}DoubleAnimation'):
                        spec = dict(animation.attrib)
                        for easing in animation.iter():
                            if easing.tag.rsplit('}', 1)[-1].endswith('Ease'):
                                spec['Easing'] = easing.tag.rsplit('}', 1)[-1]
                                spec.update(easing.attrib)
                        animations.append(spec)
                event_specs.append({'event': trigger.get('RoutedEvent'), 'animations': animations})
                continue
            if trigger.get('Property') in {'Tag', 'Source', 'Icon'} and trigger.get('Value') == '{x:Null}':
                for setter in trigger:
                    if setter.get('Property') != 'Visibility' or setter.get('Value') != 'Collapsed':
                        raise ValueError('Tag trigger needs an explicit native mapping')
                    if not setter.get('TargetName'):
                        ET.SubElement(owner, f'{{{WPF}}}Setter', {'Property':'Visibility','Value':'{Binding '+trigger.get('Property')+', RelativeSource={RelativeSource Self}, Converter={x:Static conv:ObjectConverters.IsNotNull}}'})
                        continue
                    target_node = next(e for e in template.iter() if e.get(f'{{{X}}}Name') == setter.get('TargetName'))
                    target_node.set('Visibility', '{Binding ' + trigger.get('Property') + ', RelativeSource={RelativeSource TemplatedParent}, Converter={x:Static conv:ObjectConverters.IsNotNull}}')
                continue
            data_state = None
            if kind == 'DataTrigger' and trigger.get('Value') != 'Vertical':
                binding = trigger.get('Binding')
                if all(setter.get('Property') == 'Visibility' and setter.get('Value') == 'Visible' for setter in trigger):
                    for setter in trigger:
                        target_node = next(e for e in template.iter() if e.get(f'{{{X}}}Name') == setter.get('TargetName'))
                        target_node.set('Visibility', binding)
                    continue
                ET.SubElement(owner, f'{{{WPF}}}Setter', {'Property': 'native:NativeDataCondition.Active', 'Value': binding})
                data_state = '[(native|NativeDataCondition.Active)=True]'
            conditions = list(trigger.find(f'{{{WPF}}}MultiTrigger.Conditions')) if kind == 'MultiTrigger' else []
            states = []
            for condition in conditions:
                value = {'IsMouseOver': ':pointerover', 'IsSelected': ':selected', 'IsEnabled': ':disabled', 'IsKeyboardFocused': ':focus', 'IsPressed': ':pressed', 'IsChecked': ':checked'}.get(condition.get('Property'))
                if value is None:
                    value = ':focus-visible' if 'IsKeyboardMode' in condition.get('Property', '') else ':focus-within' if condition.get('Property') == 'IsKeyboardFocusWithin' else '[' + condition.get('Property').replace(':','|') + '=' + condition.get('Value', 'True') + ']'
                if condition.get('Value') == 'False' and condition.get('Property') != 'IsEnabled':
                    value = ':not(' + value + ')'
                states.append(value)
            state = ''.join(states) if kind == 'MultiTrigger' else {'IsMouseOver': ':pointerover', 'IsPressed': ':pressed',
                     'IsFocused': ':focus', 'IsKeyboardFocused': ':focus', 'IsEnabled': ':disabled',
                     'IsExpanded': ':expanded', 'IsSelected': ':selected', 'IsHighlighted': ':selected', 'IsChecked': ':checked', 'IsKeyboardFocusWithin': ':focus-within'}.get(trigger.get('Property'))
            if data_state is not None: state = data_state
            if state is None:
                prop = trigger.get('Property', '')
                if kind == 'MultiDataTrigger': state = ':focus-within'
                elif kind == 'DataTrigger': state = '[Tag=vertical]'
                elif prop == 'HasItems':
                    state='[(native|NativeMenuFlags.HasItems)=True]'
                    ET.SubElement(owner,f'{{{WPF}}}Setter',{'Property':'native:NativeMenuFlags.Enabled','Value':'True'})
                elif prop == 'IsDragging': state = ':pressed'
                elif ':' in prop: state = '[(' + prop.replace(':', '|') + ')=' + trigger.get('Value', 'True') + ']'
                else: state = '[' + prop + '=' + trigger.get('Value', 'True') + ']'
            if trigger.get('Property') == 'IsEnabled' and trigger.get('Value') != 'False':
                state = ':not(:disabled)'
            for setter in trigger:
                if setter.tag.endswith(('}MultiTrigger.Conditions', '}MultiDataTrigger.Conditions')):
                    continue
                if setter.tag.rsplit('}', 1)[-1] in {'Trigger.EnterActions', 'Trigger.ExitActions'}:
                    entering = setter.tag.endswith('}Trigger.EnterActions')
                    for animation in setter.iter():
                        if not animation.tag.endswith('}DoubleAnimation'): continue
                        state_style = ET.SubElement(property_node, f'{{{WPF}}}Style', {'Selector': f'^{state}' if entering else '^'+state.replace('=True]','=False]') if state.startswith('[') else f'^:not({state})'})
                        ET.SubElement(state_style, f'{{{WPF}}}Setter', {'Property': animation.get('Storyboard.TargetProperty'), 'Value': animation.get('To')})
                    continue
                if setter.tag.rsplit('}', 1)[-1] != 'Setter':
                    raise ValueError(f'{name}: non-setter trigger requires native animation mapping')
                target = setter.attrib.pop('TargetName', None)
                selector = f'^{state}' if is_theme else f'{owner_type}{state}'
                if target:
                    target_node = next(e for e in template.iter() if e.get(f'{{{X}}}Name') == target)
                    target_type = target_node.tag.rsplit('}',1)[-1]
                    if target_node.tag.startswith('{clr-namespace:Roche_Scoreboard.Views}'): target_type='glass|'+target_type
                    elif target_node.tag.startswith('{clr-namespace:Roche_Scoreboard.Services}'): target_type='motion|'+target_type
                    selector += ' /template/ ' + target_type + '#' + target
                style = ET.SubElement(property_node, f'{{{WPF}}}Style', {'Selector': selector})
                style.append(setter)
        if event_specs:
            template_root = next(e for e in template if not e.tag.endswith('}ControlTemplate.Triggers'))
            template_root.set('{clr-namespace:RocheScoreboard.Mac.Native}TemplateMotion.Spec', json.dumps(event_specs, separators=(',', ':')))
        template.remove(node)
    inside_template = {id(e) for t in root.iter() if t.tag.endswith('}ControlTemplate') for e in t.iter()}
    for parent in list(root.iter()):
        for child in list(parent):
            if 'System.Windows.Shell' in child.tag: parent.remove(child)
    for e in root.iter():
        tag = e.tag.rsplit('}', 1)[-1]
        if e.tag.startswith('{clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF}'):
            e.tag = '{clr-namespace:RocheScoreboard.Mac.Native}NativeVideoSurface'
        if e.tag.startswith('{clr-namespace:System;assembly=mscorlib}'):
            e.tag = f'{{{X}}}{tag}'
        if tag == 'FontFamily' and e.text and name.startswith(('DesignSystem/', 'Resources/')):
            e.tag = '{clr-namespace:RocheScoreboard.Mac.Native}ProductionFontExtension'
            e.set('Declaration', e.text.strip())
            e.text = None
        if e.tag.startswith(f'{{{WPF}}}'):
            e.tag = f'{{{AV}}}{tag}'
        identifier = e.get(f'{{{X}}}Name')
        if identifier:
            names.append({'name': identifier, 'type': tag, 'object': tag in OBJECTS})
            if id(e) in inside_template:
                names[-1]['template'] = True
            if tag in OBJECTS:
                del e.attrib[f'{{{X}}}Name']
                e.set(f'{{{PORT}}}ProductionNames.' + ('TemplateId' if id(e) in inside_template else 'Id'), identifier)
        if tag.endswith('.ToolTip'):
            e.tag = f'{{{AV}}}ToolTip.Tip'
        if tag.endswith('.Style'):
            e.tag = f'{{{AV}}}' + tag.replace('.Style', '.Theme')
        if tag.endswith('.ItemContainerStyle'):
            e.tag = f'{{{AV}}}' + tag.replace('.ItemContainerStyle', '.ItemContainerTheme')
        if tag in {'Track.DecreaseRepeatButton', 'Track.IncreaseRepeatButton'}:
            e.tag = f'{{{AV}}}' + tag.replace('RepeatButton', 'Button')
            if len(e):
                original_name=e[0].get(f'{{{X}}}Name')
                native_name='PART_DecreaseButton' if 'Decrease' in tag else 'PART_IncreaseButton'
                if original_name:
                    e[0].set(f'{{{PORT}}}ProductionNames.TemplateId', original_name)
                    names.append({'name':original_name,'type':'RepeatButton','object':False,'native_name':native_name})
                e[0].set(f'{{{X}}}Name',native_name)
        if tag == 'AdornerDecorator': e.tag=f'{{{AV}}}Decorator'
        if tag == 'Track':
            if e.get('ViewportSize') is not None: e.set('IsDirectionReversed','{Binding Orientation,RelativeSource={RelativeSource TemplatedParent},Converter={x:Static native:NativeOrientation.VerticalReverse}}')
            for prop in ('Minimum', 'Maximum', 'Orientation', 'IsDirectionReversed'):
                if prop == 'IsDirectionReversed' and e.get('ViewportSize') is not None: continue
                e.set(prop, '{TemplateBinding ' + prop + '}')
            e.set('Value', '{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}')
        if tag == 'ContentPresenter' and id(e) in inside_template:
            ancestor=parents.get(e)
            while ancestor is not None and not ancestor.tag.endswith('}ControlTemplate'): ancestor=parents.get(ancestor)
            if ancestor is not None and ancestor.get('TargetType') == 'MenuItem':
                e.set('ContentTemplate',e.get('ContentTemplate','{TemplateBinding HeaderTemplate}'))
            e.set('Content', e.get('Content', '{TemplateBinding Content}'))
            e.set('ContentTemplate', e.get('ContentTemplate', '{TemplateBinding ContentTemplate}'))
        if tag == 'ResourceDictionary' and e.get('Source'):
            include = e.get('Source').replace('\\', '/')
            path = (source.parent / include).resolve()
            if not path.exists(): path = SRC / include.lstrip('/')
            e.tag = f'{{{AV}}}ResourceInclude'
            e.set('Source', 'avares://RocheScoreboard.Mac/Native/Generated/' + str(path.relative_to(SRC)).replace('.xaml', '.axaml'))
        if tag == 'ItemsPresenter':
            if not identifier:
                e.set(f'{{{X}}}Name', 'PART_ItemsPresenter')
            e.set('ItemsPanel', '{TemplateBinding ItemsPanel}')
        if tag == 'ScrollViewer' and identifier == 'PART_ContentHost':
            presenter = ET.SubElement(e, f'{{{WPF}}}TextPresenter', {f'{{{X}}}Name': 'PART_TextPresenter'})
            for prop in ('Text', 'CaretIndex', 'SelectionStart', 'SelectionEnd', 'TextAlignment', 'TextWrapping', 'LineHeight', 'PasswordChar', 'SelectionBrush', 'SelectionForegroundBrush', 'CaretBrush'):
                presenter.set(prop, '{TemplateBinding ' + prop + (', Mode=TwoWay}' if prop == 'Text' else '}'))
            presenter.set('HorizontalAlignment', '{TemplateBinding HorizontalContentAlignment}')
            presenter.set('VerticalAlignment', '{TemplateBinding VerticalContentAlignment}')
        if tag == 'Popup' and name == 'DesignSystem/Components' and identifier == 'popup':
            e.set(f'{{{PORT}}}ProductionNames.TemplateId', identifier)
            e.set(f'{{{X}}}Name', 'PART_Popup')
        if tag == 'Style' and 'Selector' not in e.attrib:
            target_type = e.get('TargetType')
            if not any(child.get('Property') == 'Template' for child in e if child.tag.endswith('}Setter')) and target_type in {'Button', 'CheckBox', 'RadioButton', 'Slider', 'TextBox', 'ComboBox', 'ComboBoxItem', 'ListBox', 'ListBoxItem', 'TabControl', 'TabItem', 'ScrollViewer', 'ToggleButton'}:
                e.set('BasedOn', e.get('BasedOn', '{StaticResource Native.Base.' + target_type + '}'))
            e.tag = f'{{{AV}}}ControlTheme'
            if f'{{{X}}}Key' not in e.attrib and (parents.get(e) is None or parents[e].tag.endswith('.Resources') or parents[e].tag.endswith('}ResourceDictionary')):
                e.set(f'{{{X}}}Key', '{x:Type ' + e.get('TargetType') + '}')
        if tag == 'TextBlock.LayoutTransform':
            e.tag = f'{{{AV}}}TextBlock.RenderTransform'
        if tag == 'Line':
            e.set('StartPoint', e.attrib.pop('X1', '0') + ',' + e.attrib.pop('Y1', '0'))
            e.set('EndPoint', e.attrib.pop('X2', '0') + ',' + e.attrib.pop('Y2', '0'))
            caps = [e.attrib.pop(k) for k in ('StrokeStartLineCap', 'StrokeEndLineCap') if k in e.attrib]
            if caps:
                assert len(set(caps)) == 1, 'Asymmetric line caps need an explicit native mapping'
                e.set('StrokeLineCap', caps[0])
        if tag == 'DropShadowEffect':
            depth = float(e.attrib.pop('ShadowDepth', '5'))
            direction = math.radians(float(e.attrib.pop('Direction', '315')))
            e.set('OffsetX', f'{depth * math.cos(direction):.12g}')
            e.set('OffsetY', f'{-depth * math.sin(direction):.12g}')
        for key,value in list(e.attrib.items()):
            if key=='Command' and value.startswith(('ScrollBar.','Slider.')):
                command=value.split('.')[-1]
                e.set(key,'{x:Static native:NativeRangeCommands.'+command+'}')
                e.set('CommandParameter','{Binding RelativeSource={RelativeSource AncestorType='+('ScrollBar' if value.startswith('ScrollBar.') else 'Slider')+'}}')
            if key=='Property' and value=='Command' and e.get('Value','').startswith(('ScrollBar.','Slider.')):
                e.set('Value','{x:Static native:NativeRangeCommands.'+e.get('Value').split('.')[-1]+'}')
        check_both = 'Checked' in e.attrib and e.get('Checked') == e.get('Unchecked')
        for key, value in list(e.attrib.items()):
            local = key.rsplit('}', 1)[-1]
            if key.startswith('{') and not key.startswith((f'{{{X}}}', f'{{{PORT}}}', '{clr-namespace:RocheScoreboard.Mac.Native}', '{clr-namespace:Roche_Scoreboard.Services}', '{clr-namespace:Roche_Scoreboard.Views}')):
                del e.attrib[key]
            elif local == 'Class':
                e.set(key, 'Roche_Scoreboard.MainWindow' if name == 'MainWindow' else 'Roche_Scoreboard.Views.' + name)
            elif local in EVENTS:
                del e.attrib[key]
                if local == 'MouseLeftButtonUp' and tag in {'Border','OperatorGlassSurface'} and identifier:
                    e.set('native:NativeActivation.Enabled', 'True')
                wrapper = 'Native_' + value + ('_' + local if local in {'Checked','Unchecked'} and not check_both else '')
                if EVENTS[local] in e.attrib and e.get(EVENTS[local]) != wrapper:
                    raise ValueError(f'{name}: multiple callbacks for {local} require explicit native dispatch')
                e.set(EVENTS[local], wrapper)
                event_type = {'PointerReleased': 'PointerReleasedEventArgs', 'PointerPressed': 'PointerPressedEventArgs', 'PointerEntered': 'PointerEventArgs', 'PointerExited': 'PointerEventArgs', 'PointerMoved': 'PointerEventArgs', 'PointerWheelChanged': 'PointerWheelEventArgs', 'TextInput': 'TextInputEventArgs', 'KeyDown': 'KeyEventArgs', 'SelectionChanged': 'SelectionChangedEventArgs', 'TextChanged': 'TextChangedEventArgs', 'IsCheckedChanged': 'RoutedEventArgs', 'PointerCaptureLost': 'PointerCaptureLostEventArgs', 'LostFocus': 'RoutedEventArgs'}[EVENTS[local]]
                guard = 'if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; ' if local == 'MouseLeftButtonUp' else ''
                if local in {'Checked','Unchecked'} and not check_both:
                    guard = 'if (sender is Avalonia.Controls.Primitives.ToggleButton toggle && toggle.IsChecked != ' + ('true' if local == 'Checked' else 'false') + ') return; '
                if local == 'MouseRightButtonUp':
                    guard = 'if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Right) return; '
                if local == 'MouseRightButtonDown':
                    guard = 'if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) return; '
                wrappers.append(f'    private void {wrapper}(object? sender, {event_type} e) {{ {guard}{value.replace('LostMouseCapture','PointerCaptureLost').replace('LostKeyboardFocus','LostFocus')}(sender!, e); }}')
                events.append({'name': identifier, 'source': local, 'native': EVENTS[local], 'handler': value})
            elif local in {'TextOptions.TextFormattingMode', 'TextOptions.TextHintingMode', 'TextOptions.TextRenderingMode', 'SnapsToDevicePixels', 'LineStackingStrategy'}:
                del e.attrib[key]
                changes.append({'name': identifier, 'property': local, 'source': value})
            elif local == 'Property' and value == 'TextOptions.TextFormattingMode':
                e.set(key, 'UseLayoutRounding')
                e.set('Value', 'True')
            elif local in {'FontFamily','TextElement.FontFamily'} and not value.startswith('{'):
                e.set(key, "{fonts:ProductionFont '" + value + "'}")
                changes.append({'name': identifier, 'property': local, 'source': value, 'native_fallback': 'Inter'})
            elif local == 'Property' and value in {'FontFamily','TextElement.FontFamily'} and e.get('Value') and not e.get('Value').startswith('{'):
                e.set('Value', "{fonts:ProductionFont '" + e.get('Value') + "'}")
            elif local == 'ToolTipService.ShowDuration':
                del e.attrib[key]
                e.set('{clr-namespace:RocheScoreboard.Mac.Native}TooltipDuration.Milliseconds', value)
            elif local == 'ToolTip':
                del e.attrib[key]
                e.set('ToolTip.Tip', value)
            elif local == 'PopupAnimation':
                del e.attrib[key]
                e.set('{clr-namespace:RocheScoreboard.Mac.Native}PopupMotion.Kind', value)
            elif local in {'AllowsTransparency', 'StaysOpenOnEdit'}:
                del e.attrib[key]
                changes.append({'name': identifier, 'property': local, 'source': value, 'translation': 'Native popup compositor and editable combo routing'})
            elif local == 'IsReadOnly' and value == '{TemplateBinding IsReadOnly}':
                e.set(key, 'False')
            elif local == 'Command' and value.startswith('Slider.'):
                del e.attrib[key]
            elif local == 'Property' and value == 'IsMoveToPointEnabled':
                e.set(key, 'Focusable')
                changes.append({'name': identifier, 'property': value, 'translation': 'Native slider track pointer routing'})
            elif local == 'FocusVisualStyle':
                del e.attrib[key]
                e.set('FocusAdorner', value)
            elif local == 'ItemContainerStyle':
                del e.attrib[key]
                e.set('ItemContainerTheme',value)
            elif local in {'StrokeStartLineCap','StrokeEndLineCap'}:
                del e.attrib[key]
                e.set('StrokeLineCap',value)
            elif local in {'KeyboardNavigation.DirectionalNavigation','KeyboardNavigation.ControlTabNavigation'}:
                del e.attrib[key]
                e.set('KeyboardNavigation.TabNavigation',value)
            elif local in {'FocusManager.IsFocusScope','VirtualizingPanel.IsVirtualizing','VirtualizingPanel.VirtualizationMode'}:
                del e.attrib[key]
            elif local == 'SelectedValuePath':
                del e.attrib[key]
                e.set('SelectedValueBinding','{Binding '+value+'}')
            elif local == 'Style':
                del e.attrib[key]
                e.set('Theme', value)
            elif local == 'Property' and value == 'ToolTipService.IsEnabled':
                e.set(key,'native:NativeToolTipPolicy.Enabled')
            elif local == 'Property' and value == 'ItemContainerStyle':
                e.set(key, 'ItemContainerTheme')
            elif local == 'Property' and value == 'SnapsToDevicePixels':
                e.set(key, 'UseLayoutRounding')
            elif local == 'Property' and value == 'Visibility':
                e.set(key, 'IsVisible')
                e.set('Value', str(e.get('Value') == 'Visible'))
            elif local == 'Property' and value == 'RenderTransformOrigin':
                e.set('Value', relative(e.get('Value')))
            elif local == 'IsMoveToPointEnabled':
                del e.attrib[key]
                changes.append({'name': identifier, 'property': local, 'translation': 'Native slider track pointer routing'})
            elif local == 'ResizeMode':
                del e.attrib[key]
                e.set('CanResize', str(value not in {'NoResize', 'CanMinimize'}))
            elif local == 'Property' and value in {'StrokeStartLineCap', 'StrokeEndLineCap'}:
                e.set(key, 'StrokeLineCap')
            elif local == 'Property' and value == 'StrokeLineJoin':
                e.set(key, 'StrokeJoin')
            elif tag == 'VideoView' and local == 'Background':
                del e.attrib[key]
                changes.append({'name': identifier, 'property': local, 'translation': 'Black background supplied by the production video host'})
            elif local == 'Cursor' and value in {'SizeNWSE', 'SizeNESW'}:
                e.set(key, {'SizeNWSE': 'TopLeftCorner', 'SizeNESW': 'TopRightCorner'}[value])
            elif local == 'FontWeight' and value == 'BLACK':
                e.set(key, 'Black')
            elif local == 'AlternationCount':
                del e.attrib[key]
                changes.append({'name': identifier, 'property': local, 'translation': 'Source does not bind AlternationIndex; ticker slots retain their model order'})
            elif local == 'StaysOpen':
                del e.attrib[key]
                e.set('IsLightDismissEnabled', str(value == 'False'))
            elif local == 'ContentTemplateSelector':
                del e.attrib[key]
                changes.append({'name': identifier, 'property': local, 'translation': 'Native ItemTemplate supplies the selected item data template'})
            elif local == 'ContentSource':
                del e.attrib[key]
                e.set('Content', '{TemplateBinding ' + value + '}')
                e.set('ContentTemplate', '{TemplateBinding ' + value + 'Template}')
            elif local == 'Property' and value == 'FocusVisualStyle':
                e.set(key, 'FocusAdorner')
            elif local == 'Property' and value == 'Style':
                e.set(key, 'Theme')
            elif local == 'WindowStyle':
                del e.attrib[key]
                e.set('SystemDecorations', 'Full')
            elif local == 'RenderOptions.BitmapScalingMode':
                del e.attrib[key]
                e.set('RenderOptions.BitmapInterpolationMode', 'None' if value == 'NearestNeighbor' else value)
            elif local == 'StrokeLineJoin':
                del e.attrib[key]
                e.set('StrokeJoin', value)
            elif local == 'StrokeDashArray':
                e.set(key, ','.join(value.replace(',', ' ').split()))
            elif local == 'Visibility':
                del e.attrib[key]
                e.set('IsVisible', value if value.startswith('{') else str(value == 'Visible'))
            elif local == 'Panel.ZIndex':
                del e.attrib[key]
                e.set('ZIndex', value)
            elif local in {'StartPoint', 'EndPoint', 'Center', 'GradientOrigin'} and 'Brush' in tag:
                e.set(key, relative(value))
            elif local in {'RadiusX', 'RadiusY'} and tag == 'RadialGradientBrush':
                e.set(key, f'{float(value) * 100:g}%')
            elif local == 'RenderTransformOrigin':
                e.set(key, relative(value))
            elif local in {'Viewport', 'Viewbox'} and 'Brush' in tag:
                del e.attrib[key]
                e.set('DestinationRect' if local == 'Viewport' else 'SourceRect',
                      value if e.get(local + 'Units') == 'Absolute' else relative(value))
            elif local in {'ViewportUnits', 'ViewboxUnits', 'PanningMode', 'CanContentScroll', 'CenterX', 'CenterY'}:
                del e.attrib[key]
                changes.append({'name': identifier, 'property': local, 'source': value})
    for node in root.iter():
        for key,value in list(node.attrib.items()):
            value=value.replace('#PageBefore','#PART_DecreaseButton').replace('#PageAfter','#PART_IncreaseButton')
            value=value.replace('InputGestureText','InputGesture').replace('IsSubmenuOpen','IsSubMenuOpen')
            if node.tag.endswith('}Setter') and key=='Value' and value=='Auto':value='NaN'
            value=value.replace('{x:Static SystemParameters.WorkArea}', '{x:Static native:NativeDesktop.WorkArea}')
            node.set(key,value)
    for parent in root.iter():
        caps = [child for child in parent if child.tag.endswith('}Setter') and child.get('Property') == 'StrokeLineCap']
        if len(caps) > 1:
            if len({child.get('Value') for child in caps}) != 1:
                raise ValueError('Asymmetric cap style requires native geometry mapping')
            for duplicate in caps[1:]:
                parent.remove(duplicate)
    OUT.mkdir(parents=True, exist_ok=True)
    ET.indent(root, '    ')
    (OUT / name).parent.mkdir(parents=True, exist_ok=True)
    (OUT / f'{name}.axaml').write_text('<!-- Generated from production WPF source; edit the translator. -->\n' + ET.tostring(root, encoding='unicode') + '\n')
    (OUT / f'{name}.migration.json').write_text(json.dumps({'source': str(source.relative_to(ROOT)),
        'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(), 'names': names,
        'code_source_sha256': hashlib.sha256(Path(str(source) + '.cs').read_bytes()).hexdigest() if Path(str(source) + '.cs').exists() else None,
        'native_events': events, 'platform_translations': changes,
        'visual_parity_verified': False, 'behavior_parity_verified': False}, indent=2) + '\n')
    # Named template children have a separate scope and aren't code-behind fields.
    template_names = {e.get(f'{{{X}}}Name') for template in root.iter() if template.tag.endswith('}ControlTemplate') for e in template.iter()}
    fields = []
    for n in names:
        if n.get('template', False) or n['name'] in template_names or not n['object']:
            continue
        expression = f'ProductionNames.RegistryFor(this)["{n["name"]}"]' if n['object'] else f'this.FindControl<{n["type"]}>("{n["name"]}")!'
        fields.append(f'    private {n["type"]} {n["name"]} => ({n["type"]}){expression};')
    return fields + sorted(set(wrappers))

REPLACEMENTS = {
    'System.Windows.Media.Imaging': 'Avalonia.Media.Imaging',
    'System.Windows.Shapes': 'Avalonia.Controls.Shapes',
    'System.Windows.Documents': 'Avalonia.Controls.Documents',
    'System.Windows.Media.Animation': 'RocheScoreboard.Mac.Native',
    'System.Windows.Media.Effects': 'Avalonia.Media',
    'System.Windows.Media.FontFamily': 'Avalonia.Media.FontFamily',
    'System.Windows.Media.ColorConverter': 'RocheScoreboard.Mac.Native.ColorConverter',
    'System.Windows.Controls.Orientation': 'Avalonia.Layout.Orientation',
    'System.Windows.Controls': 'Avalonia.Controls',
    'System.Windows.Input.MouseButtonEventArgs': 'Avalonia.Input.PointerEventArgs',
    'System.Windows.Input.MouseEventArgs': 'Avalonia.Input.PointerEventArgs',
    'System.Windows.Input.MouseWheelEventArgs': 'Avalonia.Input.PointerWheelEventArgs',
    'System.Windows.Input.KeyEventArgs': 'Avalonia.Input.KeyEventArgs',
    'System.Windows.MessageBox': 'RocheScoreboard.Mac.Native.NativeMessageBox',
    'TextCompositionEventArgs': 'TextInputEventArgs',
    'System.Windows.DataObject': 'RocheScoreboard.Mac.Native.NativeDataObject',
    'System.Windows.DragDropEffects': 'Avalonia.Input.DragDropEffects',
    'System.Windows.DragDrop': 'RocheScoreboard.Mac.Native.NativeDragDrop',
    'System.Windows.RoutedEventArgs': 'Avalonia.Interactivity.RoutedEventArgs',
    'System.Windows.Input': 'Avalonia.Input',
    'System.Windows.Threading': 'Avalonia.Threading',
    'System.Windows.HorizontalAlignment': 'Avalonia.Layout.HorizontalAlignment',
    'System.Windows.Media': 'Avalonia.Media',
    'System.Windows': 'Avalonia',
    'System.Windows.FlowDirection': 'Avalonia.Media.FlowDirection',
    'System.Windows.VerticalAlignment': 'Avalonia.Layout.VerticalAlignment',
    'IAnimatable': 'AvaloniaObject', 'DependencyProperty': 'AvaloniaProperty',
    'MouseButtonEventArgs': 'PointerEventArgs', 'MouseWheelEventArgs': 'PointerWheelEventArgs',
    'BitmapSource': 'NativeBitmap', 'BitmapImage': 'NativeBitmap',
    'FormatConvertedBitmap': 'NativeFormatConvertedBitmap', 'CroppedBitmap': 'NativeCroppedBitmap',
    'Int32Rect': 'PixelRect', 'ImageSource': 'IImage',
    'Avalonia.Media.PixelFormats': 'PixelFormats',
    'FrameworkElement': 'Control', 'UIElement': 'Control',
    'DependencyPropertyChangedEventArgs': 'AvaloniaPropertyChangedEventArgs',
    'DependencyObject': 'AvaloniaObject',
    'GradientStopCollection': 'GradientStops', 'FontStyles.': 'FontStyle.',
    'Timeline ': 'DoubleAnimation ', '(AnimationTimeline)': '(DoubleAnimation)',
    'FormattedText': 'NativeFormattedText',
    'SnapsToDevicePixels': 'UseLayoutRounding', 'Transform.Identity': 'new MatrixTransform(Matrix.Identity)',
    'Avalonia.Controls.StretchDirection': 'Avalonia.Media.StretchDirection',
    'FontStretches.': 'FontStretch.',
    'FontWeights.': 'FontWeight.', 'Visibility.Visible': 'true', 'Visibility.Collapsed': 'false',
    '.Visibility': '.IsVisible', '.OriginalSource': '.Source',
    'AutoUpdateService.CurrentVersion': 'ProductionVersion.CurrentVersion',
    '.ActualWidth': '.NativeWidth()', '.ActualHeight': '.NativeHeight()',
}

def native_event_subscriptions(code):
    # Keep visibility/enabled subscriptions scoped to their actual property.
    # A broad PropertyChanged rewrite would cancel workflows on unrelated changes.
    pattern = re.compile(r'(?<![\w.])(?:(?P<target>[\w.]+)\.)?(?P<event>IsVisibleChanged|IsEnabledChanged|Selected|Unselected|RequestBringIntoView)\s*(?P<op>[+-])=\s*')
    edits=[]
    for match in pattern.finditer(code):
        start=match.end();depth=0;quote=None;escape=False;end=start
        while end<len(code):
            c=code[end]
            if quote:
                if escape:escape=False
                elif c=='\\':escape=True
                elif c==quote:quote=None
            elif c in '\"\'':quote=c
            elif c in '({[':depth+=1
            elif c in ')}]':depth-=1
            elif c==';' and depth==0:break
            end+=1
        if end==len(code):raise ValueError('Unterminated native event subscription')
        event=match['event'];target=match['target'] or 'this';adding=str(match['op']=='+').lower();handler=code[start:end]
        if event == 'RequestBringIntoView':
            replacement=f'NativeEvents.BringIntoView({target},{handler});'
        elif event in {'Selected','Unselected'}:
            replacement=f'NativeEvents.WatchSelection({target},{adding},{str(event=="Selected").lower()},{handler});'
        else:
            prop='IsVisibleProperty' if event=='IsVisibleChanged' else 'IsEnabledProperty'
            replacement=f'NativeEvents.WatchProperty({target},Avalonia.Controls.Control.{prop},{adding},{handler});'
        edits.append((match.start(),end+1,replacement))
    for start,end,replacement in reversed(edits):code=code[:start]+replacement+code[end:]
    return code

def translate_code(name, fields):
    source = SRC / 'MainWindow.xaml.cs' if name == 'MainWindow' else SRC / 'Views' / f'{name}.xaml.cs'
    code = native_event_subscriptions(source.read_text(encoding='utf-8-sig'))
    code = re.sub(r'(?<![.\w])Control\b', 'TemplatedControl', code)
    for old, new in REPLACEMENTS.items():
        code = code.replace(old, new)
    code = ('// Generated from ' + str(source.relative_to(SRC)) + '; edit translator/adapters.\n'
            '// Source SHA-256: ' + hashlib.sha256(source.read_bytes()).hexdigest() + '\n'
            'using Avalonia.Threading;\nusing Avalonia.Media;\nusing Avalonia;\nusing Avalonia.Controls;\nusing Avalonia.Interactivity;\nusing Avalonia.Layout;\nusing Avalonia.Input;\nusing RocheScoreboard.Mac.Native;\n'
            + code)
    code = code.replace('Avalonia.Media.PixelFormats', 'PixelFormats')
    code = code.replace('Brush = Avalonia.Media.Brush;', 'Brush = Avalonia.Media.IBrush;')
    code = re.sub(r'\bAvalonia.Media.Brush\b', 'Avalonia.Media.IBrush', code)
    code = code.replace('VisualTreeHelper.GetDpi(sample).PixelsPerDip', '1')

    code = map_native_api(code)
    if name in {'AflPresentationGalleryWindow', 'DesignSystemGalleryWindow'}:
        code = native_gallery(code)
    if name == 'VideoPlayerControl':
        code = code.replace('_hwDecodeEnabled = true', '_hwDecodeEnabled = false').replace('EnableHardwareDecoding = true', 'EnableHardwareDecoding = false')
    if name == 'TrainingControlPanel':
        code = native_drag_handlers(code)
    code = code.replace('new Duration(', 'new NativeDuration(')
    code = re.sub(r'(?m)^(\s*)\w+\.CacheMode = .*?;[ \t]*$', r'\1{ /* Native renderer owns intermediate surface caching. */ }', code)
    code = re.sub(r'\b(\w+)\.LayoutTransform\s*=\s*([^;]+);', r'NativeLayoutTransform.Set(\1, \2);', code)
    code = re.sub(r'\b(\w+)\.LayoutTransform\b', r'NativeLayoutTransform.Get(\1)', code)
    code = re.sub(r'\bRenderOptions.SetBitmapScalingMode\([^;]+;', '', code)
    code = re.sub(r',\s*VisualTreeHelper.GetDpi\([^)]+\).PixelsPerDip\)', ')', code)
    code = re.sub(r'(?<![\w.])Dispatcher\.(BeginInvoke|InvokeAsync|Invoke|CheckAccess)', r'Dispatcher.UIThread.\1', code)
    code = code.replace('Dispatcher.UIThread.BeginInvoke', 'Dispatcher.UIThread.Post')
    code = code.replace('IsVisibleChanged +=', 'PropertyChanged +=')
    code = code.replace('Avalonia.Controls.Primitives.DragStartedEventArgs', 'Avalonia.Input.VectorEventArgs').replace('Avalonia.Controls.Primitives.DragCompletedEventArgs', 'Avalonia.Input.VectorEventArgs')
    code = code.replace('Avalonia.ExceptionRoutedEventArgs', 'Avalonia.Interactivity.RoutedEventArgs')
    code = re.sub(r'(?<![\w.])Dispatcher\.(BeginInvoke|InvokeAsync|Invoke|CheckAccess)', r'Dispatcher.UIThread.\1', code)
    code = code.replace('Dispatcher.UIThread.BeginInvoke', 'Dispatcher.UIThread.Post')
    code = re.sub(r'(?<![.\w])Dispatcher(?=\s*[,\)])', 'Dispatcher.UIThread', code)
    code = re.sub(r'using Avalonia.Interop;', '', code)
    code = code.replace('using LibVLCSharp.WPF;', 'using VideoView = RocheScoreboard.Mac.Native.NativeVideoSurface;')
    code = code.replace('LibVLCSharp.WPF.VideoView', 'NativeVideoSurface')
    code = re.sub(r'using WinForms = [^;]+;', '', code)
    code = re.sub(r'using (\w+) = ([^;]+);', lambda m: m[0] if m[1] != m[2] else '', code)
    code = code.replace('InitializeComponent();', 'NativePointer.EnsureInitialized(); ProductionNames.BeginLoad(this); try { InitializeComponent(); } finally { ProductionNames.CompleteLoad(this); }')
    init = ''
    if name == 'MainWindow':
        code = native_main_window(code)
        code = code.replace('    private bool _suppressControlSync;', '    private bool _suppressControlSync;')
        code += '\nnamespace Roche_Scoreboard { public partial class MainWindow {\n' + '\n'.join(fields) + '\n} }\n'
    else:
        code += '\nnamespace Roche_Scoreboard.Views\n{\npublic partial class ' + name + '\n{\n' + init + '\n'.join(fields) + '\n}\n}\n' if 'namespace Roche_Scoreboard.Views\n{' in code or 'namespace Roche_Scoreboard.Views\r' in code else '\npublic partial class ' + name + '\n{\n' + init + '\n'.join(fields) + '\n}\n'
    code = 'using RocheScoreboard.Mac.Production;\n' + code
    seen = set()
    lines = []
    for line in code.splitlines():
        if line.startswith('using ') and line in seen:
            continue
        if line.startswith('using '):
            seen.add(line)
        lines.append(line)
    code = '\n'.join(lines) + '\n'
    code = re.sub(r'\bobject sender\b', 'object? sender', code)
    (OUT / f'{name}.axaml.cs').write_text(code)
    print(f'{name}: source handlers retained, {len(fields)} named references')

def native_text_contract(code):
    # WPF TextBox/TextBlock expose empty strings; Avalonia permits null. Adapt
    # reads without changing assignment targets, comments or string literals.
    tokens = re.compile(r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'')
    def reads(segment):
        return re.sub(r'(?<![\w.])((?:this\.)?\w+\.(?:Text|Name))\b(?!\s*(?:=(?!=)|\+=|\?\?=))',
                      lambda m: m.group() if m.group() in {'System.Text', 'WebSocketMessageType.Text', 'item.Text'} else '(' + m.group() + ' ?? string.Empty)', segment)
    parts, offset = [], 0
    for match in tokens.finditer(code):
        parts.extend((reads(code[offset:match.start()]), match.group()))
        offset = match.end()
    parts.append(reads(code[offset:]))
    return ''.join(parts).replace('SearchBox?.Text.Trim()', '(SearchBox?.Text ?? string.Empty).Trim()')

def map_native_api(code):
    code = code.replace('using Window = NativeWindow;', 'using Window = RocheScoreboard.Mac.Native.NativeWindow;')
    code = code.replace('using SystemColors = NativeSystemColors;', 'using SystemColors = RocheScoreboard.Mac.Native.NativeSystemColors;')
    code = code.replace('using Cursors = NativeCursors;', 'using Cursors = RocheScoreboard.Mac.Native.NativeCursors;').replace('using Cursors=NativeCursors;', 'using Cursors=RocheScoreboard.Mac.Native.NativeCursors;')
    code = code.replace('Avalonia.Controls.Primitives.Avalonia.Input.', 'Avalonia.Input.')
    if 'IMultiValueConverter' in code:
        code = code.replace('Convert(object[] values,', 'Convert(IList<object?> values,').replace('values.Length', 'values.Count')
    if 'class OperatorGlassSurface' in code:
        code = code.replace('class OperatorGlassSurface : Border', 'class OperatorGlassSurface : NativePaintBorder')
        code = code.replace('_materialVisual.Effect is OperatorGlassEffect', 'false')
        code = re.sub(r'_materialVisual.Effect = !ForceFallback[^;]+;', '_materialVisual.Effect = null;', code)
        code = re.sub(r'    protected override int VisualChildrenCount[^;]+;\s*protected override Visual GetVisualChild\(int index\)\s*\{.*?\n    \}', '', code, flags=re.S)
        code = code.replace('protected override void OnRender(DrawingContext dc)', 'protected override void RenderNative(NativeDrawingContext dc)',1)
    if 'class OperatorSegmentedPanel' in code:
        code = code.replace(': UniformGrid', ': NativePaintGrid')
        code = re.sub(r'protected override void OnRender\(DrawingContext (\w+)\)', r'protected override void RenderNative(NativeDrawingContext \1)', code)
    code = re.sub(r'\[ContentProperty\(nameof\((\w+)\)\)\]\s*', '', code)
    if 'class SettingsWorkspace' in code: code = code.replace('public Avalonia.Controls.Controls Sections', '[Avalonia.Metadata.Content] public Avalonia.Controls.Controls Sections')
    code = re.sub(r'(\w+)\.CacheMode\s*=\s*([^;]+);', r'NativeCache.Set(\1, \2);', code)
    code = re.sub(r'(\w+)\.CacheMode\b', r'NativeCache.Get(\1)', code)
    code = re.sub(r'\bCacheMode\b', 'NativeCacheMode', code).replace('new BitmapCache(', 'new NativeBitmapCache(')
    code = code.replace('System.IO.FileFormatException', 'System.IO.InvalidDataException')
    code = code.replace('Binding.DoNothing', 'Avalonia.Data.BindingOperations.DoNothing')
    code = code.replace('RenderSize', 'Bounds.Size')
    code = re.sub(r'(?<![.\w])SetResourceReference\(', 'this.SetResourceReference(', code)
    code = re.sub(r'(?<![.\w])SetBinding\(', 'this.SetBinding(', code)
    code = code.replace('SystemParameters.HighContrast', 'NativeSystemPreferences.HighContrast')
    code = code.replace('IsVisibleChanged', 'PropertyChanged').replace('IsEnabledChanged', 'PropertyChanged')
    code = re.sub(r'\b\w+\.Dispatcher\b', 'Dispatcher.UIThread', code)
    code = code.replace('Dispatcher.UIThread.BeginInvoke', 'Dispatcher.UIThread.InvokeAsync')
    code = code.replace('LostKeyboardFocus', 'LostFocus').replace('GotKeyboardFocus', 'GotFocus')
    code = code.replace('PreviewKeyUp', 'KeyUp').replace('LostMouseCapture', 'PointerCaptureLost')
    code = code.replace('MouseEnter +=', 'PointerEntered +=').replace('MouseLeave +=', 'PointerExited +=')
    code = code.replace('Avalonia.Input.MouseEventHandler', 'EventHandler<Avalonia.Input.PointerEventArgs>')
    code = code.replace('RoutedEventHandler', 'EventHandler<RoutedEventArgs>').replace('MouseButtonEventHandler', 'EventHandler<Avalonia.Input.PointerPressedEventArgs>')
    code = code.replace('Viewbox = new Rect(location, new Size(width,height))', 'SourceRect = new RelativeRect(new Rect(location, new Size(width,height)), RelativeUnit.Absolute)')
    code = code.replace('PixelFormats.Pbgra32', 'PixelFormats.Bgra32').replace('Rect.Empty', 'default(Rect)')
    code = code.replace('ScrollToTop()', 'ScrollToHome()')
    code = re.sub(r'\b(\w+)\.VerticalOffset\b', r'\1.Offset.Y', code)
    code = code.replace('.ViewportWidthChange', '.ViewportDelta.X').replace('.ViewportHeightChange', '.ViewportDelta.Y').replace('.ExtentHeightChange', '.ExtentDelta.Y').replace('.ExtentWidthChange', '.ExtentDelta.X').replace('.VerticalChange', '.OffsetDelta.Y').replace('.HorizontalChange', '.OffsetDelta.X')
    code = re.sub(r'(?<![.\w])ViewportWidth\b', 'Viewport.Width', code)
    code = re.sub(r'(?<![.\w])ViewportHeight\b', 'Viewport.Height', code)
    code = code.replace('StaysOpen = true', 'IsLightDismissEnabled = false').replace('StaysOpen = false', 'IsLightDismissEnabled = true')
    code = re.sub(r'AllowsTransparency\s*=\s*(?:true|false)\s*,?', '', code)
    code = re.sub(r'AutoLayoutContent\s*=\s*(?:true|false)\s*,?', '', code)
    code = re.sub(r'ViewboxUnits\s*=\s*[^,;\s}]+\s*,?', '', code)
    code = re.sub(r'Viewbox\s*=\s*new Rect\(([^()]+)\)', r'SourceRect = new RelativeRect(\1, RelativeUnit.Relative)', code)
    code = code.replace('.Uid', '.Name')
    code = code.replace('CanContentScroll = false;', '')
    code = code.replace('Avalonia.Data.Avalonia.Data.BindingOperations', 'Avalonia.Data.BindingOperations')
    code = code.replace('pack://application:,,,/Roche Scoreboard;component/Resources/', 'avares://RocheScoreboard.Mac/Assets/')
    code = code.replace('.TranslatePoint(', '.NativeTranslatePoint(')
    code = re.sub(r'([\w.]+)\.IsEmpty\b', r'(\1.Width<=0 || \1.Height<=0)', code)
    code = re.sub(r'(?<![.\w])HasAnimatedProperties\b', 'Motion.IsAnimating(this)', code)
    code = code.replace('SystemParameters.PrimaryScreenWidth', 'NativeDesktop.WorkArea.Width').replace('SystemParameters.PrimaryScreenHeight', 'NativeDesktop.WorkArea.Height')
    code = code.replace('Avalonia.Application.Dispatcher.UIThread', 'Dispatcher.UIThread').replace('Avalonia.Application.Current.Dispatcher', 'Dispatcher.UIThread')
    code = code.replace('Avalonia.Input.NativeKeyboard', 'NativeKeyboard').replace('Avalonia.Input.Keyboard.', 'NativeKeyboard.')
    code = code.replace('FocusManager.', 'NativeFocusManager.')
    code = code.replace('Control.ForegroundProperty', 'TextBlock.ForegroundProperty').replace('Control.FontFamilyProperty', 'TextBlock.FontFamilyProperty')
    code = code.replace('Control.BackgroundProperty', 'TemplatedControl.BackgroundProperty').replace('Control.BorderBrushProperty', 'TemplatedControl.BorderBrushProperty').replace('Control.TemplateProperty','TemplatedControl.TemplateProperty')
    code = code.replace('TemplatedTextBlock.', 'TextBlock.').replace('TemplatedTemplatedControl.', 'TemplatedControl.')
    code = code.replace('BindingOperations.DoNothing','BindingOperations.DoNothing')
    code = code.replace('NativePropertyKey.AvaloniaProperty', 'NativePropertyKey.DependencyProperty').replace('HasQueryPropertyKey.AvaloniaProperty', 'HasQueryPropertyKey.DependencyProperty')
    code = re.sub(r'(?<![.\w])SetValue\(HasQueryPropertyKey,', 'this.SetValue(HasQueryPropertyKey,', code)
    code = re.sub(r'(?<![.\w])Visibility(?=\s*=)', 'IsVisible', code)
    code = code.replace('.ItemContainerGenerator.ContainerFromItem(', '.ContainerFromItem(')
    code = code.replace('PreviewMouseDown +=', 'PointerPressed +=').replace('PreviewMouseDown -=', 'PointerPressed -=')
    code = code.replace('.LocationChanged +=', '.PositionChanged +=').replace('.LocationChanged -=', '.PositionChanged -=')
    code = code.replace('MouseEnter -=', 'PointerEntered -=').replace('MouseLeave -=', 'PointerExited -=')
    code = code.replace('PreviewMouseWheel -=', 'PointerWheelChanged -=').replace('PreviewKeyDown -=', 'KeyDown -=')
    code = code.replace('RequestBringIntoView +=', 'BringIntoViewRequested +=')
    code = code.replace('KeyboardFocusChangedEventHandler', 'EventHandler<RoutedEventArgs>')
    code = re.sub(r'new Style\(([^,()]+),([^;]+?)\)', r'new NativeControlTheme(\1,\2)', code)
    code = re.sub(r'new Style\(([^,;]+),', r'new NativeControlTheme(\1,', code)
    code = re.sub(r'(?<![.\w])MouseMove(?=\s*[+-]=)', 'PointerMoved', code)
    code = re.sub(r'(?<![.\w])IsMouseOver\b', 'IsPointerOver', code)
    code = code.replace('Panel.SetZIndex(', 'NativePanel.SetZIndex(')
    code = re.sub(r'RenderingBias\s*=\s*RenderingBias.\w+\s*,?', '', code)
    code = re.sub(r'RenderOptions.SetCachingHint\([^;]+;', '', code)
    code = code.replace('new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Slider), 1)', 'new RelativeSource(RelativeSourceMode.FindAncestor){AncestorType=typeof(Slider),AncestorLevel=1}')
    code = code.replace('ToolTipService.IsEnabledProperty', 'NativeToolTipPolicy.EnabledProperty')
    code = code.replace('Avalonia.Controls.TemplatedControl', 'Avalonia.Controls.Primitives.TemplatedControl')
    code = code.replace('ControlFactory', 'NativeElementFactory')
    code = re.sub(r'\bRenderTargetBitmap\b', 'NativeRenderTargetBitmap', code)
    code = re.sub(r'\bDrawingVisual\b', 'NativeDrawingVisual', code)
    code = code.replace('Avalonia.Application.Current.Dispatcher', 'Dispatcher.UIThread')
    code = re.sub(r'if \(!root.Resources.MergedDictionaries.Any\(d => d.Source\?.OriginalString.EndsWith\("ControlCentre.xaml", StringComparison.Ordinal\) == true\)\)\s*root.Resources.MergedDictionaries.Add\(new ResourceDictionary \{ Source = new Uri\([^\n]+;', 'NativeAssets.EnsureDictionary(root,"ControlCentre");', code)
    code = code.replace('Avalonia.Controls.NativePanel', 'NativePanel')
    code = re.sub(r'(?<![.\w])Visibility(?=\s*==)', 'IsVisible', code)
    code = code.replace('SystemParameters.MinimumHorizontalDragDistance','4d').replace('SystemParameters.MinimumVerticalDragDistance','4d')
    code = code.replace('.ViewportWidth', '.Viewport.Width').replace('.ViewportHeight', '.Viewport.Height')
    code = code.replace('PreviewGotFocus', 'GotFocus')
    code = code.replace('new TextBlock', 'new NativeTextBlock')
    code = re.sub(r'new Avalonia.Controls.TextBlock\b', 'new NativeTextBlock', code)
    code = re.sub(r'\b(\w+)\.ToolTip\?\.ToString\(\)', r'Avalonia.Controls.ToolTip.GetTip(\1)?.ToString()', code)
    code = re.sub(r'(?<![\w.])(\w+)\.ToolTip(?=[;,\)])', r'Avalonia.Controls.ToolTip.GetTip(\1)', code)
    code = code.replace('Selector.SelectedEvent', 'NativeSelection.SelectedEvent').replace('Selector.UnselectedEvent', 'NativeSelection.UnselectedEvent').replace('Selector.SelectionChangedEvent', 'Avalonia.Controls.Primitives.SelectingItemsControl.SelectionChangedEvent')
    code = code.replace('EventManager.RegisterClassHandler', 'NativeEventManager.RegisterClassHandler')
    code = code.replace('.ItemContainerGenerator.StatusChanged', '.LayoutUpdated')
    code = code.replace('StartLineCap =', 'LineCap =').replace('EndLineCap =', 'LineCap =')
    code = re.sub(r'LineCap\s*=\s*PenLineCap.Round\s*,\s*LineCap\s*=\s*PenLineCap.Round', 'LineCap = PenLineCap.Round', code)
    code = code.replace('StrokeStartLineCap =', 'StrokeLineCap =').replace('StrokeEndLineCap =', 'StrokeLineCap =')
    code = re.sub(r'StrokeLineCap\s*=\s*PenLineCap.Round\s*,\s*StrokeLineCap\s*=\s*PenLineCap.Round', 'StrokeLineCap = PenLineCap.Round', code)
    code = code.replace('Avalonia.Controls.Control.BackgroundProperty','Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty')
    code = code.replace('new DispatcherTimer(DispatcherPriority.Background)', 'new DispatcherTimer()').replace('new DispatcherTimer(DispatcherPriority.Input)', 'new DispatcherTimer()')
    code = re.sub(r'(\w+)\.IsMouseCaptureWithin',r'NativePointer.IsCaptureWithin(\1)',code)
    code = code.replace('if (!brush.IsFrozen) brush.Freeze();', 'brush.Freeze();')
    code = code.replace('Point _originalOrigin', 'RelativePoint _originalOrigin')
    code = re.sub(r'(RenderTransformOrigin\s*=\s*)new (?:Avalonia\.)?Point\(([^,()]+),([^()]+)\)', r'\1new RelativePoint(\2,\3,RelativeUnit.Relative)', code)
    code = re.sub(r'new RelativeSource\(RelativeSourceMode.FindAncestor,\s*typeof\((\w+)\),\s*(\d+)\)', r'new RelativeSource(RelativeSourceMode.FindAncestor){AncestorType=typeof(\1),AncestorLevel=\2}', code)
    code = code.replace('Avalonia.Controls.Nativechild', 'child').replace('Avalonia.Controls.Nativetile','tile')
    code = re.sub(r'(?<![.\w])PointToScreen\(', 'this.PointToScreen(', code)
    code = re.sub(r'(\w+)\??\.Template\?\.FindName\(([^,]+),\s*\w+\)', r'NativeTemplates.FindObject(\1,\2)', code)
    code = re.sub(r'(\w+)\??\.Template.FindName\(([^,]+),\s*\w+\)', r'NativeTemplates.FindObject(\1,\2)', code)
    code = re.sub(r'(void (?:Blur|LostFocus)\(object\?? (?:sender|s), )GotFocusEventArgs', r'\1RoutedEventArgs', code)
    code = re.sub(r'(void LostCapture\(object\?? (?:sender|s), )PointerEventArgs', r'\1PointerCaptureLostEventArgs', code)
    code = re.sub(r'using TemplatedControl\s*=\s*Avalonia.Controls.Control;', 'using TemplatedControl = Avalonia.Controls.Primitives.TemplatedControl;', code)
    code = code.replace('Nativetile.', 'tile.').replace('Nativechild.', 'child.')
    code = code.replace('PenLineCap =', 'LineCap =')
    code = code.replace('StartLineCap=', 'LineCap=').replace('EndLineCap=', 'LineCap=')
    code = re.sub(r'LineCap=PenLineCap.Round,LineCap=PenLineCap.Round', 'LineCap=PenLineCap.Round', code)
    code = code.replace('StrokeStartLineCap=', 'StrokeLineCap=').replace('StrokeEndLineCap=', 'StrokeLineCap=')
    code = re.sub(r'StrokeLineCap=PenLineCap.Round,StrokeLineCap=PenLineCap.Round','StrokeLineCap=PenLineCap.Round',code)
    code = code.replace('DashStyles.Dash', 'DashStyle.Dash').replace('DashStyles.Dot','DashStyle.Dot')
    code = code.replace('window.WindowStyle!=WindowStyle.None', '!((window as NativeWindow)?.IsOutputWindow??false)')
    code = re.sub(r'window.WindowStyle\s*=\s*WindowStyle.None;', 'window.SystemDecorations=SystemDecorations.Full;', code)
    code = re.sub(r'(void (?:Blur|LostFocus)\([^,]+,\s*)(?:Avalonia.Input.)?GotFocusEventArgs', r'\1RoutedEventArgs', code)
    code = re.sub(r'(void LostCapture\([^,]+,\s*)(?:Avalonia.Input.)?PointerEventArgs', r'\1PointerCaptureLostEventArgs', code)
    code = re.sub(r'(void Refresh\([^,]+,\s*)(?:Avalonia.Input.)?PointerEventArgs', r'\1RoutedEventArgs', code)
    code = code.replace('e.NewFocus','e.Source').replace('e.OldFocus','e.Source')
    code = code.replace('DependencyPropertyHelper.', 'NativeValueSource.')
    code = code.replace('BaseValueSource.', 'NativeBaseValueSource.')
    code = code.replace('tip.PlacementTarget', 'NativeToolTipPolicy.PlacementTarget(tip)')
    code = code.replace('this.PointToScreen(new Point(', 'this.PointToScreen(new Point(')
    code = re.sub(r'(?<![.\w])Visibility(?=\s*[!=]=)', 'IsVisible', code)
    code = code.replace('Visibility !=', 'IsVisible !=').replace('Visibility==', 'IsVisible==')
    code = re.sub(r'(?<![.\w])Visibility\b(?=\s*\))', 'IsVisible', code)
    code = code.replace('Avalonia.Window', 'RocheScoreboard.Mac.Native.NativeWindow').replace('Avalonia.SizeChangedEventArgs', 'Avalonia.Controls.SizeChangedEventArgs')
    code = code.replace('Avalonia.Controls.Primitives.ButtonBase', 'Avalonia.Controls.Button')
    code = code.replace('Avalonia.Controls.ControlTemplate', 'Avalonia.Markup.Xaml.Templates.ControlTemplate')
    code = code.replace('Avalonia.Data.IValueConverter', 'Avalonia.Data.Converters.IValueConverter')
    code = code.replace('Avalonia.SystemColors', 'NativeSystemColors').replace('Avalonia.Input.Cursors', 'NativeCursors')
    code = code.replace('System.ComponentModel.AvaloniaPropertyDescriptor', 'NativePropertyDescriptor')
    code = code.replace('KeyboardFocusChangedEventArgs', 'Avalonia.Input.GotFocusEventArgs')
    code = code.replace('ControlCollection', 'Avalonia.Controls.Controls').replace('AvaloniaPropertyKey', 'NativePropertyKey')
    code = code.replace('Freezable', 'AvaloniaObject').replace('DragStartedEventArgs', 'Avalonia.Input.VectorEventArgs').replace('DragCompletedEventArgs', 'Avalonia.Input.VectorEventArgs')
    code = re.sub(r'(?<![.\w])Visibility(?=\s+\w)', 'bool', code)
    code = code.replace('AvaloniaProperty.Register', 'NativeProperty.Register')
    code = code.replace('public override void OnApplyTemplate()', 'public override void OnNativeApplyTemplate()').replace('base.OnApplyTemplate()', 'base.OnNativeApplyTemplate()')
    code = code.replace('OnPreviewKeyDown(', 'OnNativePreviewKeyDown(').replace('OnGotKeyboardFocus(', 'OnNativeGotFocus(')
    code = code.replace('OnTextChanged(', 'OnNativeTextChanged(')
    code = code.replace('class OperatorSearchBox : TextBox', 'class OperatorSearchBox : NativeSearchTextBox')
    code = re.sub(r'protected override void OnRender\(DrawingContext (\w+)\)\s*\{', r'public override void Render(Avalonia.Media.DrawingContext nativeContext) { using var \1 = new NativeDrawingContext(nativeContext);', code)
    code = code.replace('base.OnRender(', 'NativeDrawingContext.RenderBase(')
    code = re.sub(r'(?<![.\w])DrawingContext(?= \w+)', 'NativeDrawingContext', code)
    code = re.sub(r'new Point\(([^(),]+),\s*([^(),]+)\)(?=;)', r'new Point(\1, \2)', code)
    code = code.replace('Avalonia.Media.new MatrixTransform(Matrix.Identity)', 'new MatrixTransform(Matrix.Identity)')
    code = native_text_contract(code)
    code = code.replace('Windows x64', 'Mac').replace('Windows-x64', 'Mac')
    code = re.sub(r'\bobject (sender|s)(?=, (?:\w+\.)*\w*EventArgs\b)', r'object? \1', code)
    code = re.sub(r'\(Avalonia.Styling.ControlTheme\)this.FindResource\(([^)]+)\)', r'NativeResources.Require<Avalonia.Styling.ControlTheme>(this, \1)', code)
    code = code.replace('var dotText = (TextBlock)dot.Child;', 'var dotText = NativeObjects.Require<TextBlock>(dot.Child);')
    code = code.replace('var st = (ScaleTransform)pip.RenderTransform;', 'var st = NativeObjects.Require<ScaleTransform>(pip.RenderTransform);')
    code = code.replace('var brush = (SolidColorBrush)pip.Background;', 'var brush = NativeBrush.Require<SolidColorBrush>(pip.Background);')
    code = code.replace('Avalonia.Media.IBrush prevBrush =', 'Avalonia.Media.IBrush? prevBrush =').replace('Brush prevBrush =', 'Brush? prevBrush =')
    code = code.replace('foreach (object item in comboBox.Items)', 'foreach (object? item in comboBox.Items)')
    code = code.replace('IsInsideButton(AvaloniaObject d)', 'IsInsideButton(AvaloniaObject? d)')
    code = code.replace('picker.InitialDirectory = Path.GetDirectoryName(initialPath);', 'picker.InitialDirectory = Path.GetDirectoryName(initialPath) ?? string.Empty;')
    code = code.replace('global::Avalonia.RoutedEventArgs', 'Avalonia.Interactivity.RoutedEventArgs')
    code = re.sub(r'(\bBorder \w+\s*=\s*)new\(\)', r'\1new NativeBorder()', code)
    code = code.replace('new Button {', 'new NativeButton {')
    code = code.replace('StyleProperty', 'ThemeProperty')
    code = code.replace('Avalonia.Controls.Primitives.TextBoxBase.TextChangedEvent', 'TextBox.TextChangedEvent')
    code = code.replace('TextChangedEventHandler', 'EventHandler<TextChangedEventArgs>')
    code = re.sub(r'(\w+)\?\.IsMouseCaptured', r'NativePointer.IsCaptured(\1)', code)
    code = re.sub(r'(\w+)\.IsMouseCaptured', r'NativePointer.IsCaptured(\1)', code)
    code = code.replace('.LostMouseCapture +=', '.PointerCaptureLost +=')
    code = code.replace('Avalonia.Application.Current.Dispatcher', 'Avalonia.Threading.Dispatcher.UIThread')
    code = code.replace('NativeNativeMessageBox', 'NativeMessageBox')
    code = re.sub(r'RoutedPropertyChangedEventArgs<[^>]+>', 'Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs', code)
    code = re.sub(r'(?<![\w])Window.GetWindow\(', 'NativeWindow.GetWindow(',code)
    code = code.replace('Avalonia.Application.Current?.MainWindow', 'NativeDesktop.MainWindow').replace('Application.Current.MainWindow', 'NativeDesktop.MainWindow')
    code = re.sub(r': Window\b', ': NativeWindow', code)
    code = re.sub(r'(?<![\w.])Window\? owner\b', 'Avalonia.Controls.Window? owner', code)
    code = code.replace('Microsoft.Win32.OpenFileDialog', 'NativeOpenFileDialog')
    code = code.replace('Microsoft.Win32.SaveFileDialog', 'NativeSaveFileDialog')
    code = re.sub(r'(?<![\w.])OpenFileDialog\b', 'NativeOpenFileDialog', code)
    code = re.sub(r'(?<![\w.])SaveFileDialog\b', 'NativeSaveFileDialog', code)
    code = re.sub(r'using Microsoft.Win32;', '', code)
    code = code.replace('Avalonia.MessageBox', 'NativeMessageBox').replace('MessageBoxButton.', 'NativeMessageBoxButton.').replace('MessageBoxImage.', 'NativeMessageBoxImage.')
    code = re.sub(r'(?<![\w.])SystemParameters.WorkArea', 'NativeDesktop.WorkArea', code)
    code = re.sub(r'(\w+)\.CaptureMouse\(\)', r'NativePointer.Capture(\1)', code)
    code = re.sub(r'(\w+)\.ReleaseMouseCapture\(\)', r'NativePointer.Release(\1)', code)
    code = re.sub(r'(\w+)\.LeftButton != MouseButtonState.Pressed', r'!\1.GetCurrentPoint(null).Properties.IsLeftButtonPressed', code)
    code = re.sub(r'(\w+)\.LeftButton == MouseButtonState.Pressed', r'\1.GetCurrentPoint(null).Properties.IsLeftButtonPressed', code)
    code = code.replace('.MouseLeftButtonDown +=', '.PointerPressed +=').replace('.MouseLeftButtonUp +=', '.PointerReleased +=')
    code = code.replace('.PreviewMouseLeftButtonDown +=', '.PointerPressed +=').replace('.PreviewMouseMove +=', '.PointerMoved +=')
    code = code.replace('.MouseMove +=', '.PointerMoved +=').replace('.MouseEnter +=', '.PointerEntered +=').replace('.MouseLeave +=', '.PointerExited +=')
    code = re.sub(r'(?:Avalonia.Input.)?Cursors\.(Arrow|Hand|SizeAll|Cross|IBeam)', r'new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.\1)', code)
    code = re.sub(r'using Cursors = [^;]+;', '', code)
    code = code.replace('Keyboard.Focus(', 'NativeKeyboard.Focus(').replace('Keyboard.FocusedElement', 'NativeKeyboard.FocusedElement')
    code = code.replace('Avalonia.Input.Cursors.Hand', 'new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)')
    code = code.replace('NativeNativeMessageBox', 'NativeMessageBox')
    code = code.replace('Avalonia.Media.Imaging.NativeBitmap', 'NativeBitmap').replace('Avalonia.Media.Imaging.BitmapCacheOption', 'BitmapCacheOption')
    code = re.sub(r'(?<![\w.])Visibility(?=\s*(?:=|!=|==))', 'IsVisible', code)
    code = re.sub(r'(?<![\w.])BeginAnimation\(', 'this.BeginAnimation(', code)
    code = code.replace('new Line ', 'new NativeLine ').replace('new Line\n', 'new NativeLine\n').replace('new Polyline', 'new NativePolyline')
    code = code.replace('DoubleCollection', 'Avalonia.Collections.AvaloniaList<double>')
    code = re.sub(r'ShadowDepth = 0(?=\s*})', 'OffsetX = 0, OffsetY = 0', code)
    code = re.sub(r'PresentationSource.FromVisual\(([^)]+)\)', r'TopLevel.GetTopLevel(\1)', code)
    code = re.sub(r'new (?:Avalonia.Media.)?FontFamily\(', 'NativeFonts.Family(', code)
    code = code.replace('Environment.GetFolderPath(', 'NativeStoragePaths.GetFolderPath(')
    code = code.replace('MainWindow.GenerateQrCode', 'NativeQr.GenerateQrCode')
    code = code.replace('SystemParameters.ClientAreaAnimation', 'NativeSystemPreferences.ClientAreaAnimation')
    code = code.replace('Avalonia.Media.VisualTreeHelper', 'VisualTreeHelper')
    code = code.replace('Avalonia.Controls.Control.ForegroundProperty', 'TextBlock.ForegroundProperty').replace('Avalonia.Controls.Control.BackgroundProperty', 'Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty')
    code = code.replace('.IsKeyboardFocused', '.IsFocused').replace('.IsMouseOver', '.IsPointerOver')
    code = re.sub(r'(?<![\w.])MessageBoxResult\b', 'NativeMessageBoxResult', code)
    code = re.sub(r'\bStyle\s*=', 'Theme =', code).replace('(Style)', '(Avalonia.Styling.ControlTheme)')
    code = re.sub(r'\(Avalonia.Styling.ControlTheme\)this.FindResource\(([^)]+)\)', r'NativeResources.Require<Avalonia.Styling.ControlTheme>(this, \1)', code)
    code = re.sub(r'(?<![\w.])TryFindResource\(', 'this.TryFindResource(', code)
    code = re.sub(r'(?<![\w.])FindResource\(', 'this.FindResource(', code)
    code = re.sub(r'\(Avalonia.Styling.ControlTheme\)this.FindResource\(([^)]+)\)', r'NativeResources.Require<Avalonia.Styling.ControlTheme>(this, \1)', code)
    code = re.sub(r'\bnew Button\b', 'new NativeButton', code).replace('new Border', 'new NativeBorder')
    code = re.sub(r'(\w+)\.ToolTip = ([^;]+);', r'Avalonia.Controls.ToolTip.SetTip(\1, \2);', code)
    code = code.replace('.PreviewTextInput +=', '.TextInput +=').replace('PreviewKeyDown +=', 'KeyDown +=')
    code = code.replace('e.Effects', 'e.DragEffects')
    code = re.sub(r'(\w+).ContextMenu.IsOpen = true;', r'\1.ContextMenu.Open(\1);', code)
    code = re.sub(r'(Add(?:Home|Away)Player_Click\(object\?? sender, )PointerEventArgs', r'\1RoutedEventArgs', code)
    code = code.replace('new PointerEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)', 'e')
    code = code.replace('double y = origin.Y - 1.5;', 'double y = (origin?.Y ?? 0) - 1.5;')
    code = code.replace('Avalonia.Controls.Primitives.DragStartedEventArgs', 'Avalonia.Input.VectorEventArgs').replace('Avalonia.Controls.Primitives.DragCompletedEventArgs', 'Avalonia.Input.VectorEventArgs')
    code = code.replace('Avalonia.ExceptionRoutedEventArgs', 'Avalonia.Interactivity.RoutedEventArgs')
    code = re.sub(r'(?<![\w.])Dispatcher\.(BeginInvoke|InvokeAsync|Invoke|CheckAccess)', r'Dispatcher.UIThread.\1', code)
    code = code.replace('Dispatcher.UIThread.BeginInvoke', 'Dispatcher.UIThread.Post')
    code = re.sub(r'(?<![.\w])Dispatcher(?=\s*[,\)])', 'Dispatcher.UIThread', code)
    code = re.sub(r'using Avalonia.Interop;', '', code)
    code = code.replace('using LibVLCSharp.WPF;', 'using VideoView = RocheScoreboard.Mac.Native.NativeVideoSurface;')
    code = code.replace('LibVLCSharp.WPF.VideoView', 'NativeVideoSurface')
    code = code.replace('Avalonia.Application.Current is App', 'Avalonia.Application.Current is RocheScoreboard.Mac.App').replace('Avalonia.Application.Current is not App', 'Avalonia.Application.Current is not RocheScoreboard.Mac.App')
    code = code.replace('compatible Windows release', 'compatible Mac release').replace('Windows release', 'Mac release')
    code = code.replace('IsVisibleChanged -=', 'PropertyChanged -=')
    code = re.sub(r'\bAvalonia.Control\b', 'Avalonia.Controls.Control', code.replace('Avalonia.NativeWindow', 'NativeWindow'))
    code = code.replace('Avalonia.Input.Keyboard.Modifiers', 'NativeKeyboard.Modifiers').replace('Keyboard.Modifiers', 'NativeKeyboard.Modifiers')
    code = code.replace('NativeNativeKeyboard', 'NativeKeyboard').replace('Avalonia.Input.ModifierKeys', 'Avalonia.Input.KeyModifiers').replace('ModifierKeys.', 'KeyModifiers.')
    code = code.replace('Keyboard.ClearFocus()', 'NativeKeyboard.ClearFocus()')
    code = re.sub(r'\?\? ((?:Avalonia.Media.)?(?:Brushes|MediaBrushes))\.(\w+)', r'?? new SolidColorBrush(\1.\2.Color)', code)
    code = code.replace('CompositionTarget.Rendering', 'NativeRendering.Rendering')
    code = code.replace('PreviewMouseWheel +=', 'PointerWheelChanged +=').replace('.SourceInitialized +=', '.Opened +=')
    code = code.replace('PreviewMouseLeftButtonDown +=', 'PointerPressed +=').replace('PreviewMouseLeftButtonUp +=', 'PointerReleased +=')
    code = code.replace('.RequestBringIntoView +=', '.BringIntoViewRequested +=')
    code = code.replace('Visibility = true', 'IsVisible = true').replace('Visibility = false', 'IsVisible = false')
    code = re.sub(r'\bAnimatable\b', 'AvaloniaObject', code)
    code = code.replace('new Avalonia.Media.LinearGradientBrush(', 'NativeBrush.Linear(')
    code = code.replace('Avalonia.false', 'false').replace('Avalonia.true', 'true')
    code = code.replace('Avalonia.GridLength', 'Avalonia.Controls.GridLength').replace('Avalonia.GridUnitType', 'Avalonia.Controls.GridUnitType')
    code = code.replace('Avalonia.VerticalAlignment', 'Avalonia.Layout.VerticalAlignment')
    code = code.replace('Avalonia.FlowDirection', 'Avalonia.Media.FlowDirection')
    code = code.replace('Avalonia.Controls.StretchDirection', 'Avalonia.Media.StretchDirection')
    code = re.sub(r'(?<![.\w])ActualWidth\b', 'Bounds.Width', code)
    code = re.sub(r'(?<![.\w])ActualHeight\b', 'Bounds.Height', code)
    code = re.sub(r'(RenderTransformOrigin|StartPoint|EndPoint|Center|GradientOrigin)\s*=\s*new (?:Avalonia\.)?Point\(([^()]*)\)', r'\1 = new RelativePoint(\2, RelativeUnit.Relative)', code)
    code = re.sub(r'(StartPoint|EndPoint)\s*=\s*new\(([^()]*)\)', r'\1 = new RelativePoint(\2, RelativeUnit.Relative)', code)
    code = code.replace('new LinearGradientBrush(', 'NativeBrush.Linear(')
    code = re.sub(r'(?:Avalonia.Controls.)?Panel.SetZIndex\((\w+), ([^)]+)\)', r'\1.ZIndex = \2', code)
    code = re.sub(r'(\w+)\.ShadowDepth = 0;', r'\1.OffsetX = \1.OffsetY = 0;', code)
    code = code.replace('ShadowDepth = 0,', 'OffsetX = 0, OffsetY = 0,')
    code = re.sub(r'^\s*LineStackingStrategy = LineStackingStrategy.\w+,?\n', '', code, flags=re.M)
    code = re.sub(r'\((LinearGradientBrush|RadialGradientBrush)\)([^;\n]+)\.(Fill|Background);', r'NativeBrush.Require<\1>(\2.\3);', code)
    code = re.sub(r'(\w+)\.Inlines.Clear\(\)', r'NativeText.ClearInlines(\1)', code)
    code = re.sub(r'(\w+)\.Inlines.Add\(', r'\1.Inlines!.Add(', code)
    code = re.sub(r'(\w+)\.Template.FindName\(([^,]+),\s*\1\)', r'NativeTemplates.Find(\1, \2)', code)
    code = re.sub(r'(?<![\w.])FindName\(', 'NativeLookup.Find(this, ', code)
    # Already mapped template lookups must not be rewritten a second time.
    code = code.replace('___UNUSED_FindName(', 'NativeLookup.Find(this, ')
    code = code.replace('var source = PresentationSource.FromVisual(HomeNameText);', 'var source = TopLevel.GetTopLevel(HomeNameText);')
    code = code.replace('source?.CompositionTarget != null', 'source != null').replace('source.CompositionTarget.TransformToDevice.M11', 'source.RenderScaling')
    code = re.sub(r'\bNative(?=tile\b|child\b|_videoPlayer\b|_retroCrtOverlay\b|_calibrationOverlay\b|veil\b|preview\b)', '', code)
    code = code.replace('NativeNativeWindow', 'NativeWindow')
    code = code.replace('PreviewMouseLeftButtonDown -=', 'PointerPressed -=').replace('PreviewMouseLeftButtonUp -=','PointerReleased -=')
    code = code.replace('.MouseEnter +=', '.PointerEntered +=').replace('.MouseLeave +=','.PointerExited +=')
    code = re.sub(r'(void (?:Blur|LostFocus)\([^,]+,\s*)(?:Avalonia.Input.)?GotFocusEventArgs', r'\1RoutedEventArgs', code)
    code = code.replace('args.NewFocus','args.Source').replace('args.OldFocus','args.Source')
    code = code.replace('PreviewKeyDown +=', 'KeyDown +=')
    code = code.replace('PropertyPath =', 'PropertyPath =')
    code = code.replace('Dispatcher.UIThread.Post(new Action(() => { _refresh', 'Dispatcher.UIThread.InvokeAsync(new Action(() => { _refresh')
    code = code.replace('DispatcherPriority.Render', 'DispatcherPriority.Background')
    code = code.replace('new ImageBrush(', 'new NativeImageBrush(')
    code = code.replace('Avalonia.Media.ImageBrush(', 'NativeImageBrush(')
    code = code.replace('new RadialGradientBrush(', 'NativeBrush.Radial(')
    code = re.sub(r'\.IsFrozen\s*\?', '.IsFrozen ? ', code)
    code = re.sub(r'(\w+)\.IsFrozen', r'NativeImmutability.IsFrozen(\1)', code)
    code = re.sub(r'if \((\w+)\.CanFreeze\)', 'if (true)', code)
    code = re.sub(r'Avalonia.Media.RenderOptions.SetCacheInvalidationThreshold(?:Minimum|Maximum)\([^;]+;', '', code)
    code = re.sub(r'(?<![\w.])RenderOptions.SetBitmapScalingMode\([^;]+;', '', code)
    code = re.sub(r'Avalonia.Media\.\s*\n\s*', '', code)
    code = code.replace('RenderCapability.Tier', '0')
    code = code.replace('NativeCacheMode=new NativeBitmapCache(.75),','').replace('NativeCacheMode = new NativeBitmapCache(.75),','')
    code = code.replace('origin?.Y ?? 0','origin.Y')
    code = code.replace('OperatorSearchBox: TextBox', 'OperatorSearchBox: NativeSearchTextBox')
    code = code.replace('new LinearGradientBrush()', 'new LinearGradientBrush()')
    code = re.sub(r'new NativeImageBrush\(([^)]+)\)\s*\{([^}]+)\}', lambda m: 'NativeImageBrush.Create(' + m[1] + ', brush => {' + ';'.join('brush.'+part.strip() for part in m[2].split(',') if part.strip()) + ';})', code)
    code = code.replace('Avalonia.Controls.Primitives.PlacementMode','Avalonia.Controls.PlacementMode')
    code = code.replace('ItemContainerStyle=', 'ItemContainerTheme=').replace('ItemContainerStyle =','ItemContainerTheme =')
    code = re.sub(r'new DispatcherTimer\((?:Avalonia.Threading.)?DispatcherPriority.\w+\)', 'new DispatcherTimer()', code)
    code = code.replace('Geometry.Empty','new StreamGeometry()')
    code = code.replace('AvaloniaPropertyHelper.', 'NativeValueSource.')
    code = re.sub(r'(?<![\w])Window.GetWindow\b', 'NativeWindow.GetWindow',code)
    code = code.replace('window as NativeNativeWindow', 'window as NativeWindow')
    code = code.replace('WindowChromeEnforcer.Attach(this);', 'OperatorWindowChrome.Enable(this);')
    code = code.replace('Control.PreviewMouseMoveEvent', 'Avalonia.Input.InputElement.PointerMovedEvent')
    code = code.replace('Control.MouseLeaveEvent', 'Avalonia.Input.InputElement.PointerExitedEvent')
    code = code.replace('Control.PreviewMouseLeftButtonDownEvent', 'Avalonia.Input.InputElement.PointerPressedEvent')
    code = code.replace('Control.PreviewMouseLeftButtonUpEvent', 'Avalonia.Input.InputElement.PointerReleasedEvent')
    code = code.replace('Control.GotFocusEvent', 'Avalonia.Input.InputElement.GotFocusEvent').replace('Control.LostFocusEvent','Avalonia.Input.InputElement.LostFocusEvent')
    code = re.sub(r'(RenderTransformOrigin\s*=\s*)new (?:Avalonia\.)?Point\(([^,()]+),([^()]+)\)', r'\1new RelativePoint(\2,\3,RelativeUnit.Relative)', code)
    code = code.replace('new Point(.5,.5)', 'new Point(.5,.5)')
    code = re.sub(r'(void (?:Blur|LostFocus)\([^,]+,\s*)(?:Avalonia.Input.)?GotFocusEventArgs', r'\1RoutedEventArgs', code)
    code = code.replace('group.Children.Add(original);','group.Children.Add(original as Transform??new MatrixTransform(original?.Value??Matrix.Identity));')
    code = code.replace('private readonly List<(TextBlock Text,Transform Original,TransformGroup Applied)>','private readonly List<(TextBlock Text,ITransform Original,TransformGroup Applied)>')
    code = code.replace('Transform? original,', 'ITransform? original,').replace('Transform original,', 'ITransform original,')
    code = re.sub(r'new RelativeSource\(RelativeSourceMode.FindAncestor,\s*typeof\(([^)]+)\),\s*(\d+)\)', r'new RelativeSource(RelativeSourceMode.FindAncestor){AncestorType=typeof(\1),AncestorLevel=\2}',code)
    code = code.replace('if (Visibility', 'if (IsVisible').replace('if(Visibility', 'if(IsVisible')
    code = code.replace('if(!IsVisible)', 'if(!IsVisible)')
    code = code.replace('KeyboardNavigation.SetControlTabNavigation','KeyboardNavigation.SetTabNavigation')
    code = re.sub(r'(\w+)\.HorizontalScrollBarVisibility\s*=\s*ScrollBarVisibility.Hidden;', r'ScrollViewer.SetHorizontalScrollBarVisibility(\1,ScrollBarVisibility.Hidden);',code)
    code = re.sub(r'field.InputScope = new InputScope[^;]+;', 'Avalonia.Input.TextInput.TextInputOptions.SetContentType(field,overs?Avalonia.Input.TextInput.TextInputContentType.Number:Avalonia.Input.TextInput.TextInputContentType.Digits);', code)
    code = code.replace('box.SelectionLength', '(box.SelectionEnd-box.SelectionStart)').replace('field.SelectionLength', '(field.SelectionEnd-field.SelectionStart)')
    code = re.sub(r'(?<![.\w])Visibility(?=\s*is\b)', 'IsVisible', code)
    code = code.replace('form.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))','NativeNavigation.FocusFirst(form)')
    code = code.replace('RadiusX=.7,RadiusY=.9', 'RadiusX=new RelativeScalar(.7,RelativeUnit.Relative),RadiusY=new RelativeScalar(.9,RelativeUnit.Relative)')
    code = code.replace('point.Y-=motion.Y','point=new Avalonia.Point(point.X,point.Y-motion.Y)')
    code = code.replace('args.NewFocus','args.Source').replace('args.OldFocus','args.Source')
    code = re.sub(r'(?<![.\w])BeginAnimation\(', 'this.BeginAnimation(',code)
    code = code.replace('Avalonia.Media.new StreamGeometry()', 'new StreamGeometry()')
    code = code.replace('new GeometryDrawing(', 'NativeDrawing.Geometry(')
    code = re.sub(r'ViewportUnits\s*=\s*BrushMappingMode.Absolute\s*,?', '', code)
    code = re.sub(r'Viewport\s*=\s*new Rect\(([^()]+)\)', r'DestinationRect=new RelativeRect(\1,RelativeUnit.Absolute)',code)
    code = code.replace('new RelativeRect(0,0,160,148, RelativeUnit.Relative)','new RelativeRect(0,0,160,148, RelativeUnit.Absolute)')
    code = re.sub(r'(?<![\w])Visibility.Hidden\b','false',code)
    code = code.replace('new DispatcherTimer(DispatcherPriority.Background, Dispatcher.UIThread)', 'new DispatcherTimer()')
    code = code.replace('RenderTransformOrigin.X', 'RenderTransformOrigin.Point.X').replace('RenderTransformOrigin.Y','RenderTransformOrigin.Point.Y')
    code = re.sub(r'(\w+)\.HasAnimatedProperties',r'Motion.IsAnimating(\1)',code)
    code = code.replace('if(brush.CanFreeze)', 'if(true)')
    code = code.replace('private Transform? _original;', 'private ITransform? _original;')
    code = code.replace('var ink = category', 'IBrush ink = category')
    code = code.replace('origin.X-=prefixAdvance;', 'origin=new Point(origin.X-prefixAdvance,origin.Y);')
    code = re.sub(r'(?<![.\w])RenderOptions.SetCacheInvalidationThreshold(?:Minimum|Maximum)\([^;]+;', '', code)
    code = re.sub(r'(\w+)\.ToolTip\s*=([^;]+);',r'Avalonia.Controls.ToolTip.SetTip(\1,\2);',code)
    code = re.sub(r'new Avalonia.Data.RelativeSource\(Avalonia.Data.RelativeSourceMode.FindAncestor,typeof\(([^)]+)\),(\d+)\)',r'new Avalonia.Data.RelativeSource(Avalonia.Data.RelativeSourceMode.FindAncestor){AncestorType=typeof(\1),AncestorLevel=\2}',code)
    code = re.sub(r'\b\w+\.Dispatcher.UIThread', 'Dispatcher.UIThread', code)
    code = code.replace('Viewbox=source', 'SourceRect=new RelativeRect(source,RelativeUnit.Absolute)')
    code = code.replace('StrokeLineJoin =', 'StrokeJoin =').replace('StrokeLineJoin=', 'StrokeJoin=')
    code = code.replace('.MouseEnter+=', '.PointerEntered+=').replace('.MouseLeave+=','.PointerExited+=')
    code = code.replace('previousField?.SelectionLength', '(previousField is null?0:previousField.SelectionEnd-previousField.SelectionStart)').replace('_scorecardScroll?.VerticalOffset','_scorecardScroll?.Offset.Y')
    code = re.sub(r'(void Toggle(?:Home|Away)Wicketkeeper_Click\([^,]+,\s*)PointerEventArgs',r'\1RoutedEventArgs',code)
    code = code.replace('new TextBox { Tag = "scorecard-fow-entry"','new NativeSearchTextBox { Tag = "scorecard-fow-entry"')
    code = re.sub(r'new WpfKeyEventArgs\(NativeKeyboard.PrimaryDevice, TopLevel.GetTopLevel\(this\)!, 0, Avalonia.Input.Key.Right\)\s*\{', 'new WpfKeyEventArgs { Key=Avalonia.Input.Key.Right,',code)
    code = code.replace('NativeKeyboard.KeyDownEvent', 'InputElement.KeyDownEvent')
    code = code.replace('action.PreviewKeyDown+=', 'action.KeyDown+=')
    code = re.sub(r'new MouseEventArgs\(Mouse.PrimaryDevice,\s*0\)', 'new RoutedEventArgs()',code)
    code = re.sub(r'(void (?:Card|TrainingTile|SettingsTile)_Mouse(?:Enter|Leave)\([^,]+,\s*)(?:PointerEventArgs|MouseEventArgs)',r'\1RoutedEventArgs',code)
    code = code.replace('RegisterName(name,action)', 'NativeNameScope.Register(this,name,action)')
    code = code.replace('Control.PointerCaptureLostEvent,new EventHandler<Avalonia.Input.PointerEventArgs>', 'InputElement.PointerCaptureLostEvent,new EventHandler<Avalonia.Input.PointerCaptureLostEventArgs>')
    code = code.replace('InputElement.PointerReleasedEvent,new EventHandler<Avalonia.Input.PointerPressedEventArgs>', 'InputElement.PointerReleasedEvent,new EventHandler<Avalonia.Input.PointerReleasedEventArgs>')
    code = code.replace('SelectionChangedEventHandler', 'EventHandler<SelectionChangedEventArgs>')
    code = code.replace('RoutedPropertyChangedEventHandler<double>', 'EventHandler<RangeBaseValueChangedEventArgs>')
    code = code.replace('(pair.Text ?? string.Empty).RenderTransform', 'pair.Text.RenderTransform')
    code = code.replace('ViewportUnits=', 'ViewportUnits=')
    code = code.replace('transform.Children.Add(RenderTransform);','transform.Children.Add(RenderTransform as Transform??new MatrixTransform(RenderTransform?.Value??Matrix.Identity));')
    code = re.sub(r'StrokeLineCap = PenLineCap.Square,\s*StrokeLineCap = PenLineCap.Square,','StrokeLineCap = PenLineCap.Square,',code)
    code = code.replace('new Avalonia.Controls.ComboBoxItem { Content = page.Version, Tag = page.Version, ToolTip = FormatReleaseDate(page.Date, false) }','NativeToolTipPolicy.WithTip(new Avalonia.Controls.ComboBoxItem { Content = page.Version, Tag = page.Version },FormatReleaseDate(page.Date, false))')
    code = code.replace('WindowChromeEnforcer.Attach(window);','OperatorWindowChrome.Enable(window);')
    code = re.sub(r'(?<![\w])Window.GetWindow\(root\)', 'NativeWindow.GetWindow(root)',code)
    code = code.replace('IsOperatorWindow(ownerWindow)','ownerWindow is Window owningWindow && IsOperatorWindow(owningWindow)')
    code = re.sub(r'(?:Native){2,}Window','NativeWindow',code)
    code = code.replace('private readonly Transform _original;', 'private readonly ITransform? _original;')
    code = code.replace('new ResourceDictionary { Source = new Uri("/Roche Scoreboard;component/Resources/OperatorControls.xaml", UriKind.Relative) }','NativeAssets.Dictionary("OperatorControls")')
    code = code.replace('surface.RenderTransformOrigin=new Avalonia.Point(Math.Clamp(origin.X/surface.NativeWidth(),.12,.88),Math.Clamp(origin.Y/surface.NativeHeight(),.08,.92))','surface.RenderTransformOrigin=new RelativePoint(Math.Clamp(origin.X/surface.NativeWidth(),.12,.88),Math.Clamp(origin.Y/surface.NativeHeight(),.08,.92),RelativeUnit.Relative)')
    code = code.replace('_surface.RenderTransformOrigin = Origin()', '_surface.RenderTransformOrigin = new RelativePoint(Origin(),RelativeUnit.Relative)')
    code = code.replace('args.IsRepeat', 'NativeKeyboard.IsRepeated(args)')
    code = code.replace('NativeswipeText', 'swipeText')
    code = re.sub(r'if \(previousStatus[^\n]*AutomationPeer.ListenerExists[^\n]*\)\s*\{.*?peer\?.RaiseAutomationEvent\([^;]+;\s*\}', 'Avalonia.Automation.AutomationProperties.SetLiveSetting(_scorecardStatus,Avalonia.Automation.AutomationLiveSetting.Polite);',code,flags=re.S)
    code = code.replace('ToolTip.OpenedEvent','Control.LoadedEvent')
    code = code.replace('NativeWindow.GetWindow(current)', 'NativeWindow.GetWindow(current as Control??root)')
    code = code.replace('(previousField is null?0:previousField.SelectionEnd-previousField.SelectionStart) ?? 0','(previousField is null?0:previousField.SelectionEnd-previousField.SelectionStart)')
    code = code.replace('SystemParameters.StaticPropertyChanged','NativeSystemPreferences.StaticPropertyChanged')
    code = code.replace('var dispatcher = Avalonia.Application.Current?.Dispatcher','var dispatcher = Dispatcher.UIThread')
    code = code.replace('dispatcher.BeginInvoke(', 'dispatcher.InvokeAsync(')
    code = code.replace('button.ToolTip is string', 'Avalonia.Controls.ToolTip.GetTip(button) is string')
    code = code.replace('_transforms.Children.Add(_original)', '_transforms.Children.Add(_original as Transform??new MatrixTransform(_original?.Value??Matrix.Identity))')
    code = code.replace('NativeWindow.GetWindow(current as Control??root)', 'NativeWindow.GetWindow(current)')
    code = code.replace(' || dispatcher.HasShutdownStarted','')
    code = re.sub(r'var tabButtons\s*=\s*new\[\]', 'var tabButtons = new Button[]', code)
    code = code.replace('new NativeButton[', 'new Button[')
    code=code.replace('new NativeDrawingContext(nativeContext)', 'new NativeDrawingContext(nativeContext,Bounds.Size)')
    code = re.sub(r'((?:public|private|internal|protected) static readonly )AvaloniaProperty(\s+\w+\s*=\s*)NativeProperty.(Register(?:Attached)?)\(([^\n]*?typeof\(([^)]+)\))',lambda m:m[1]+'AttachedProperty<'+m[5]+'>'+m[2]+'(AttachedProperty<'+m[5]+'>)NativeProperty.'+m[3]+'('+m[4],code)
    code = re.sub(r'((?:public|private|internal|protected) static readonly )AvaloniaProperty(\s+\w+\s*=\s*)NativeProperty.(Register(?:Attached)?)\(\s*("[^"\n]+"|nameof\(\w+\))\s*,\s*typeof\(([^)]+)\)',lambda m:m[1]+'AttachedProperty<'+m[5]+'>'+m[2]+'(AttachedProperty<'+m[5]+'>)NativeProperty.'+m[3]+'('+m[4]+',typeof('+m[5]+')',code)
    code = code.replace('AvaloniaProperty HasQueryProperty = HasQueryPropertyKey.DependencyProperty','AttachedProperty<bool> HasQueryProperty = (AttachedProperty<bool>)HasQueryPropertyKey.DependencyProperty')
    if 'class SettingsWorkspace' in code:code=code.replace('public Avalonia.Controls.Controls Sections', '[Avalonia.Metadata.Content] public Avalonia.Controls.Controls Sections')
    code = re.sub(r'(void \w+_(?:PointerCaptureLost|LostFocus)\([^,]+,\s*)(?:Avalonia.Input.)?(?:MouseEventArgs|PointerEventArgs|GotFocusEventArgs)', r'\1RoutedEventArgs', code)
    return code.replace('using SystemColors = NativeSystemColors;', 'using SystemColors = RocheScoreboard.Mac.Native.NativeSystemColors;').replace('using Cursors = NativeCursors;', 'using Cursors = RocheScoreboard.Mac.Native.NativeCursors;').replace('using Cursors=NativeCursors;', 'using Cursors=RocheScoreboard.Mac.Native.NativeCursors;').replace('Avalonia.Controls.Primitives.Avalonia.Input.', 'Avalonia.Input.').replace('NativeNativeWindow', 'NativeWindow').replace('Nativenew ', 'new ').replace('bool Visibility)', 'bool IsVisible)')

def native_drag_handlers(code):
    code = code.replace('row.AllowDrop = true;', 'Avalonia.Input.DragDrop.SetAllowDrop(row, true);')
    code = code.replace('row.PointerMoved += (s, e) =>', 'row.PointerMoved += async (s, e) =>')
    code = code.replace('DragDrop.DoDragDrop(row, data, DragDropEffects.Move);', 'await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);')
    code = code.replace('e.Data.GetDataPresent(DragDataFormat)', 'NativeDragData.Contains(e.DataTransfer, DragDataFormat)')
    code = code.replace('e.Data.GetData(DragDataFormat)', 'NativeDragData.Get(e.DataTransfer, DragDataFormat)')
    # Match each lambda block without changing the scoring / reorder statements.
    for event in ('DragOver', 'Drop'):
        start = code.index('row.' + event + ' +=')
        brace = code.index('{', start)
        depth, index = 1, brace + 1
        while depth:
            if code[index] == '{': depth += 1
            elif code[index] == '}': depth -= 1
            index += 1
        segment = code[start:index + 1]
        segment = segment.replace('row.' + event + ' +=', 'row.AddHandler(Avalonia.Input.DragDrop.' + event + 'Event,', 1)
        segment = segment[:-1] + ');'
        code = code[:start] + segment + code[index + 1:]
    return code.replace('using SystemColors = NativeSystemColors;', 'using SystemColors = RocheScoreboard.Mac.Native.NativeSystemColors;').replace('using Cursors = NativeCursors;', 'using Cursors = RocheScoreboard.Mac.Native.NativeCursors;').replace('using Cursors=NativeCursors;', 'using Cursors=RocheScoreboard.Mac.Native.NativeCursors;').replace('Avalonia.Controls.Primitives.Avalonia.Input.', 'Avalonia.Input.').replace('NativeNativeWindow', 'NativeWindow').replace('Nativenew ', 'new ').replace('bool Visibility)', 'bool IsVisible)')

def native_lineup_drag(code):
    code=code.replace('AllowDrop = true','')
    code=re.sub(r'(var card = new Grid[^\n]+;)',r'\1 Avalonia.Input.DragDrop.SetAllowDrop(card,true);',code)
    code=code.replace('handle.PointerMoved += (_, e) =>', 'handle.PointerMoved += async (_, e) =>')
    code=code.replace('DragDrop.DoDragDrop(card, "cricket-lineup-" + team, DragDropEffects.Move)', 'await NativeDragDrop.DoDragDropAsync(e,new NativeDataObject("roche-cricket-lineup","cricket-lineup-"+team),DragDropEffects.Move)')
    code=code.replace('e.Data.GetDataPresent(typeof(string))','NativeDragData.Contains(e.DataTransfer,"roche-cricket-lineup")')
    code=code.replace('e.Data.GetData(typeof(string))','NativeDragData.Get(e.DataTransfer,"roche-cricket-lineup")')
    code=code.replace('e.Key == Key.System ? e.SystemKey : e.Key','e.Key')
    code=code.replace('var tabButtons = new[]','var tabButtons = new Button[]')
    for event in ('DragOver','DragLeave','Drop'):
        token='card.Preview'+event+' +='
        start=code.index(token);brace=code.index('{',start);depth=1;end=brace+1
        while depth:
            if code[end]=='{':depth+=1
            elif code[end]=='}':depth-=1
            end+=1
        segment=code[start:end+1].replace(token,'card.AddHandler(Avalonia.Input.DragDrop.'+event+'Event,',1)
        segment=segment[:-1]+',RoutingStrategies.Tunnel|RoutingStrategies.Bubble,true);'
        code=code[:start]+segment+code[end+1:]
    return code

def native_gallery(code):
    code = 'using App = RocheScoreboard.Mac.App;\n' + code
    code = re.sub(r'new\((width), (height), 96, 96, PixelFormats.Pbgra32\)', r'new(new PixelSize(\1, \2), new Vector(96, 96))', code)
    code = re.sub(r'\s*PngBitmapEncoder encoder = new\(\);\s*encoder.Frames.Add\(BitmapFrame.Create\(bitmap\)\);', '', code)
    code = re.sub(r'this.TryFindResource\((\w+)\)', r'NativeResources.FindOrNull(this, \1)', code)
    code = code.replace('encoder.Save(output);', 'bitmap.Save(output);')
    code = re.sub(r'new WpfKeyEventArgs\(Avalonia.Input.Keyboard.PrimaryDevice, (?:PresentationSource.FromVisual|TopLevel.GetTopLevel)\(this\)!, 0, Avalonia.Input.Key.Right\)', 'new WpfKeyEventArgs { Key = Avalonia.Input.Key.Right }', code)
    code = code.replace('new WpfKeyEventArgs { Key = Avalonia.Input.Key.Right }\n        {', 'new WpfKeyEventArgs\n        {\n            Key = Avalonia.Input.Key.Right,')
    code = code.replace('Avalonia.Input.Keyboard.KeyDownEvent', 'Avalonia.Input.InputElement.KeyDownEvent')
    code = code.replace('TooltipSampleButton.ToolTip', 'Avalonia.Controls.ToolTip.GetTip(TooltipSampleButton)')
    code = code.replace('toolTip.IsOpen = true;', 'Avalonia.Controls.ToolTip.SetIsOpen(TooltipSampleButton, true);').replace('toolTip.IsOpen = false;', 'Avalonia.Controls.ToolTip.SetIsOpen(TooltipSampleButton, false);')
    code = code.replace('bool open = toolTip.IsOpen;', 'bool open = Avalonia.Controls.ToolTip.GetIsOpen(TooltipSampleButton);')
    return code.replace('using SystemColors = NativeSystemColors;', 'using SystemColors = RocheScoreboard.Mac.Native.NativeSystemColors;').replace('using Cursors = NativeCursors;', 'using Cursors = RocheScoreboard.Mac.Native.NativeCursors;').replace('using Cursors=NativeCursors;', 'using Cursors=RocheScoreboard.Mac.Native.NativeCursors;').replace('Avalonia.Controls.Primitives.Avalonia.Input.', 'Avalonia.Input.').replace('NativeNativeWindow', 'NativeWindow').replace('Nativenew ', 'new ').replace('bool Visibility)', 'bool IsVisible)')

def native_auto_update(code):
    start = code.index('    private async Task<bool> DownloadAndApplyCoreAsync(')
    body = code.index('{', start)
    end = code.index('    private static string BuildUpdaterScript', body)
    close = code.rfind('}', body, end)
    code = code[:body + 1] + """
        LastError = null;
        try { return await MacUpdater.ApplyAsync(LatestRelease is null ? null : FindAsset(LatestRelease), progress, ct); }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or System.Security.SecurityException)
        {
            LastError = ex.Message;
            WriteDiagnostic("Mac update failed: " + ex);
            return false;
        }
    """ + code[close:]
    start = code.index('    private static GitHubReleaseAsset? FindAsset(')
    end = code.index('    private sealed class LegacyProgressAdapter', start)
    code = code[:start] + '    private static GitHubReleaseAsset? FindAsset(GitHubRelease release) => MacUpdater.FindAsset(release);\n\n' + code[end:]
    # The native installer owns validation, replacement and rollback. Do not
    # carry unreachable Windows launchers or their Windows-only QA into Mac.
    start = code.index('    private static string BuildUpdaterScript(')
    end = code.index('    private static GitHubReleaseAsset? FindAsset(', start)
    code = code[:start] + code[end:]
    start = code.index('    internal static IReadOnlyDictionary<string, bool> RunSafetyChecks()')
    end = code.index('    public static string GetReleasePageUrl()', start)
    code = code[:start] + code[end:]
    start = code.index('    private static async Task DownloadFileWithRetryAsync(')
    end = code.index('    private static void WriteDiagnostic(', start)
    code = code[:start] + code[end:]
    code = code.replace('    private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(45);\n', '')
    code = code.replace('    private const int DownloadAttempts = 3;\n', '')
    code = code.replace('    private const long MaximumExpandedPackageBytes = 2L * 1024 * 1024 * 1024;\n', '')
    code = re.sub(r'    /// <summary>\n    /// Builds the PowerShell updater script\..*?    /// </summary>\n', '', code, flags=re.S)
    code = code.replace('stages it outside the application folder, and launches a PowerShell', 'stages it outside the application folder, and launches a native Mac')
    code = code.replace('Writes a log to <c>%TEMP%\\Roche_Scoreboard_Update.log</c>', 'Writes a log in the per-user Roche_Scoreboard data directory')
    return code.replace('using SystemColors = NativeSystemColors;', 'using SystemColors = RocheScoreboard.Mac.Native.NativeSystemColors;').replace('using Cursors = NativeCursors;', 'using Cursors = RocheScoreboard.Mac.Native.NativeCursors;').replace('using Cursors=NativeCursors;', 'using Cursors=RocheScoreboard.Mac.Native.NativeCursors;').replace('Avalonia.Controls.Primitives.Avalonia.Input.', 'Avalonia.Input.').replace('NativeNativeWindow', 'NativeWindow').replace('Nativenew ', 'new ').replace('bool Visibility)', 'bool IsVisible)')

def native_main_window(code):
    # Native output windows provide their own resize borders; the Win32 HWND
    # hooks and DWM frame suppression have no counterpart or purpose on Mac.
    code = re.sub(r'Avalonia.Shell.WindowChrome.SetWindowChrome\([^;]+?\}\);', '', code, flags=re.S)
    code = re.sub(r'\s*var source = \(HwndSource\)(?:PresentationSource.FromVisual|TopLevel.GetTopLevel)\([^)]+\)!;\s*source.AddHook\(\w+\);', '', code)
    code = re.sub(r'\s*Services.WindowChromeEnforcer.Attach\([^)]+\);', '', code)
    code = code.replace('new Window\n', 'new NativeWindow\n').replace('new Window {', 'new NativeWindow {')
    code = re.sub(r'IntPtr hwnd = new WindowInteropHelper\(this\).Handle;\s*SendMessage\([^;]+;', 'BeginResizeDrag(Avalonia.Controls.WindowEdge.SouthEast, (Avalonia.Input.PointerPressedEventArgs)e);', code)
    code = code.replace('WindowStyle = WindowStyle.None,', 'SystemDecorations = Avalonia.Controls.SystemDecorations.Full,')
    code = code.replace('ResizeMode = ResizeMode.CanResizeWithGrip,', 'CanResize = true,')
    code = re.sub(r'AllowsTransparency = [^,\n]+,', '', code)
    code = re.sub(r'MainContent.BringIntoViewRequested \+= \(_, e\) =>\s*\{.*?\};', 'MainContent.AddHandler(Control.RequestBringIntoViewEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);', code, flags=re.S)
    code = code.replace('Visibility specialistVisibility', 'bool specialistVisibility').replace('{ Visibility: true }', '{ IsVisible: true }')
    code = code.replace('MatchSetupTab.ToolTip?.ToString()', 'Avalonia.Controls.ToolTip.GetTip(MatchSetupTab)?.ToString()')
    code = re.sub(r'ViewboxUnits = (?:Avalonia.Media.)?BrushMappingMode.RelativeToBoundingBox,', '', code)
    code = code.replace('ShowActivated = false,', '')
    code = re.sub(r'(?<![\w.])Window\b', 'NativeWindow', code)
    code = code.replace('Avalonia.Controls.NativeWindow', 'NativeWindow')
    code = re.sub(r'ResizeMode = ResizeMode.\w+,', 'CanResize = true,', code)
    code = code.replace('_retroCrtOverlay.RenderTransform = new ScaleTransform(1, 1, 0, height / 2);', '_retroCrtOverlay.RenderTransformOrigin = new RelativePoint(0, .5, RelativeUnit.Relative);\n        _retroCrtOverlay.RenderTransform = new ScaleTransform(1, 1);')
    code = re.sub(r'^\s*Avalonia.Media.RenderOptions.Set(?:CachingHint|CacheInvalidationThresholdMinimum|CacheInvalidationThresholdMaximum)\([^;]+;\n', '', code, flags=re.M)
    code = code.replace('AutoLayoutContent = false,', '').replace('ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,', '')
    code = code.replace('Viewbox = new Rect(0, 0, 1, 1)', 'SourceRect = new RelativeRect(0, 0, 1, 1, RelativeUnit.Relative)')
    code = code.replace('Avalonia.Clipboard.SetText(', 'NativeClipboard.SetText(')
    if '    private IntPtr DisplayWndProc(' in code:
        start = code.index('    private IntPtr DisplayWndProc(')
        end = code.index('    private bool _isBreakRotating;', start)
        code = code[:start] + '    private const int MIN_TRACK_PX = 50;\n\n' + code[end:]
    code = re.sub(r'    \[DllImport\("user32.dll"[^\n]*\n    private static extern IntPtr SendMessage[^\n]*\n\s*private const uint WM_NCLBUTTONDOWN[^\n]*\n', '', code)
    return code.replace('using SystemColors = NativeSystemColors;', 'using SystemColors = RocheScoreboard.Mac.Native.NativeSystemColors;').replace('using Cursors = NativeCursors;', 'using Cursors = RocheScoreboard.Mac.Native.NativeCursors;').replace('using Cursors=NativeCursors;', 'using Cursors=RocheScoreboard.Mac.Native.NativeCursors;').replace('Avalonia.Controls.Primitives.Avalonia.Input.', 'Avalonia.Input.').replace('NativeNativeWindow', 'NativeWindow').replace('Nativenew ', 'new ').replace('bool Visibility)', 'bool IsVisible)')

def native_interaction_motion(code):
    start = code.index('        root.AddHandler(')
    end = code.index('    }', start)
    code = code[:start] + """
        Control? hover = null;
        root.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, (sender, e) =>
        {
            var next = FindInteractive(e.Source as AvaloniaObject, root);
            if (ReferenceEquals(next, hover)) return;
            if (hover is not null) AnimateTo(hover, GetState(hover), 1, 0, 210, SmoothOut);
            hover = next;
            OnMouseEnter(sender!, e);
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        root.PointerExited += (_, _) => { if (hover is not null) AnimateTo(hover, GetState(hover), 1, 0, 210, SmoothOut); hover = null; };
        root.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnPointerDown, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, OnPointerUp, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(Button.ClickEvent, OnButtonClick, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        root.AddHandler(Avalonia.Input.InputElement.GotFocusEvent, OnKeyboardFocus, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        root.AddHandler(Avalonia.Input.InputElement.LostFocusEvent, OnKeyboardFocusLost, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
""" + code[end:]
    code = code.replace('Avalonia.Controls.Primitives.ButtonBase', 'Button')
    code = code.replace('KeyboardFocusChangedEventArgs', 'Avalonia.Interactivity.RoutedEventArgs').replace('RoutedEventArgs e', 'Avalonia.Interactivity.RoutedEventArgs e')
    code = code.replace('Avalonia.Interactivity.Avalonia.Interactivity.', 'Avalonia.Interactivity.')
    code = code.replace('e.NewFocus', 'e.Source').replace('e.OldFocus', 'e.Source')
    code = code.replace('Animatable target', 'AvaloniaObject target')
    code = code.replace('Transform existing = target.RenderTransform;', 'var existing = target.RenderTransform;')
    code = code.replace('group.Children.Add(existing);', 'group.Children.Add(existing as Transform ?? new MatrixTransform(existing?.Value??Matrix.Identity));')
    code = code.replace('element.Cursor == new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)', 'element.Cursor?.ToString() == "Hand"')
    return 'using Avalonia.Controls;\nusing Avalonia.Interactivity;\n' + code

def native_logo_conversion(code):
    a = code.index('        NativeRenderTargetBitmap rendered =')
    b = code.index('        bool hasTransparency', a)
    code = code[:a] + '        int stride = width * 4;\n        byte[] pixels = NativeRaster.RenderPixels(image, width, height);\n\n' + code[b:]
    a = code.index('        PngBitmapEncoder encoder =')
    b = code.index('    }', a)
    code = code[:a] + '        using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);\n        result.Save(output);\n' + code[b:]
    code = code.replace('image.Width', 'image.Size.Width').replace('image.Height', 'image.Size.Height')
    return code.replace('using SystemColors = NativeSystemColors;', 'using SystemColors = RocheScoreboard.Mac.Native.NativeSystemColors;').replace('using Cursors = NativeCursors;', 'using Cursors = RocheScoreboard.Mac.Native.NativeCursors;').replace('using Cursors=NativeCursors;', 'using Cursors=RocheScoreboard.Mac.Native.NativeCursors;').replace('Avalonia.Controls.Primitives.Avalonia.Input.', 'Avalonia.Input.').replace('NativeNativeWindow', 'NativeWindow').replace('Nativenew ', 'new ').replace('bool Visibility)', 'bool IsVisible)')

def translate_support(relative):
    path = SRC / relative
    code = native_event_subscriptions(path.read_text(encoding='utf-8-sig'))
    code = re.sub(r'(?<![.\w])Control\b', 'TemplatedControl', code)
    override = ROOT / 'macos/native-overrides' / relative
    if override.exists():
        destination = OUT / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text('// Generated from ' + relative + '; native platform equivalent in macos/native-overrides.\n// Source SHA-256: ' + hashlib.sha256(path.read_bytes()).hexdigest() + '\n' + override.read_text())
        return
    for old, new in REPLACEMENTS.items():
        code = code.replace(old, new)
    code = code.replace('Avalonia.Media.PixelFormats', 'PixelFormats')
    code = code.replace('Brush = Avalonia.Media.Brush;', 'Brush = Avalonia.Media.IBrush;')
    code = re.sub(r'\bAvalonia.Media.Brush\b', 'Avalonia.Media.IBrush', code)
    code = map_native_api(code)
    code = code.replace('Avalonia.Media.BrushConverter', 'NativeBrushConverter').replace('new BrushConverter()', 'new NativeBrushConverter()')
    if relative == 'Views/OperatorViewport.cs':
        code=code.replace('public class OperatorViewport : ScrollViewer\n{','public class OperatorViewport : ScrollViewer\n{\n    protected override Type StyleKeyOverride=>typeof(ScrollViewer);')
    if relative == 'Views/CricketControlPanel.Workflows.cs':
        code=native_lineup_drag(code)
    if relative == 'Services/CricketReliabilityChecks.cs':
        start=code.index('    internal static CricketMatchManager CreateMatch')
        header=code[:code.index('{',code.index('internal static class CricketReliabilityChecks'))+1]
        code=header+'\n'+code[start:]
    if relative == 'Views/CricketControlPanel.PlayerMenu.cs':
        code = re.sub(r'\(Avalonia.Markup.Xaml.Templates.ControlTemplate\)Avalonia.Markup.XamlReader.Parse\(\"\"\".*?\"\"\"\)', 'NativeMenuTemplate.Create()', code, flags=re.S)
        code = code.replace('Avalonia.Markup.Xaml.Templates.ControlTemplate?', 'Avalonia.Controls.Templates.IControlTemplate?')
        code = re.sub(r'CustomPopupPlacementCallback\s*=.*?\]\s*', 'VerticalOffset=8,', code, flags=re.S)
        code = code.replace('PlacementMode.Custom', 'PlacementMode.Bottom')
        code = re.sub(r'PopupAnimation\s*=\s*PopupAnimation.None,', '', code)
    if relative.startswith('MainWindow.'):
        code = native_main_window(code)
        code = 'using Avalonia.Controls;\nusing Avalonia.Media;\nusing Avalonia.Layout;\nusing Avalonia.Input;\nusing Avalonia.Interactivity;\nusing Avalonia.Threading;\nusing Avalonia.Controls.Primitives;\n' + code
    if relative == 'Views/EmergencyVideoWindow.cs':
        code = code.replace('EnableHardwareDecoding = true', 'EnableHardwareDecoding = false')
    if relative == 'Services/VlcRuntime.cs':
        code = code.replace('Core.Initialize();', 'MacVlcRuntime.Initialize();')
    if relative == 'Services/AutoUpdateService.cs':
        code = native_auto_update(code)
    if relative == 'Services/InteractionMotion.cs' and 'root.AddHandler(' in code:
        code = native_interaction_motion(code)
    if relative == 'Services/WindowLayoutStorage.cs':
        code = re.sub(r'\bWindow\b', 'NativeWindow', code).replace('!rb.IsEmpty', 'rb.Width > 0 && rb.Height > 0')
    if relative == 'Views/EmergencyVideoWindow.cs':
        code = code.replace('WindowStyle = WindowStyle.None;', 'SystemDecorations = SystemDecorations.Full;').replace('ResizeMode = ResizeMode.NoResize;', 'CanResize = false;').replace('AllowsTransparency = false;', '')
        code = code.replace('new VideoView { Background = Brushes.Black }', 'new VideoView()').replace('VideoView videoView = new() { Background = Brushes.Black };', 'VideoView videoView = new();')
    if relative == 'Services/VideoOptimizer.cs':
        code = code.replace('ffmpeg.exe', 'ffmpeg')
    if relative == 'Services/TeamLogoService.Bundled.cs':
        code = re.sub(r'var resource = Avalonia.Application.GetResourceStream\(new Uri\(([^;]+?)\)\)\s*\?\? throw new IOException\([^;]+;', r'var resource = NativeAssets.Open(new Uri(\1));', code)
        code = code.replace('resource.Stream','resource')
        a=code.index('        var frame = BitmapDecoder.Create(')
        b=code.index('        int width = converted.PixelWidth',a)
        code=code[:a]+"        using var decoded = new NativeBitmap { StreamSource = input }; decoded.BeginInit(); decoded.EndInit();\n        if (Math.Max(decoded.PixelWidth, decoded.PixelHeight)>1024) { decoded.DecodePixelWidth=decoded.PixelWidth>=decoded.PixelHeight?1024:0; decoded.DecodePixelHeight=decoded.PixelHeight>decoded.PixelWidth?1024:0; input.Position=0; decoded.EndInit(); }\n        var converted = decoded;\n"+code[b:]
        code=code.replace('var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result));','')
        code=code.replace('encoder.Save(file)', 'result.Save(file)')
    if relative == 'Services/TeamLogoService.cs':
        code = native_logo_conversion(code)
    code = re.sub(r'using WinForms = [^;]+;', '', code)
    code = 'using RocheScoreboard.Mac.Native;\nusing Avalonia;\nusing Avalonia.Media;\nusing Avalonia.Layout;\nusing Avalonia.Controls;\nusing Avalonia.Controls.Primitives;\nusing Avalonia.Threading;\n' + code
    seen = set()
    lines = []
    for line in code.splitlines():
        if line.startswith('using ') and line in seen:
            continue
        if line.startswith('using '):
            seen.add(line)
        lines.append(line)
    code = '\n'.join(lines) + '\n'
    destination = OUT / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text('// Generated from ' + relative + '; edit translator/adapters.\n// Source SHA-256: ' + hashlib.sha256(path.read_bytes()).hexdigest() + '\n' + code)

if __name__ == '__main__':
    for stale in OUT.rglob('*'):
        if stale.is_file() and stale.suffix in {'.cs', '.axaml', '.json'}: stale.unlink()
    # Inventory runtime support automatically: 5.0 splits controllers and models
    # across partials. Keep platform services explicit and do not omit new partials.
    native = {'Services/ThemeService.cs', 'Services/PixelSizeHelper.cs', 'Services/ImageLoadHelper.cs',
              'Services/WindowChromeEnforcer.cs', 'Services/EyeDropperService.cs', 'Services/TooltipDuration.cs',
              'Controls/MarqueeTextBlock.cs', 'App.xaml.cs', 'AssemblyInfo.cs', 'Services/EyeDropper.cs', 'Services/ChangelogService.cs', 'Services/NestedScrollRouting.cs', 'Views/ResponsivePresentationTypography.cs', 'Services/AflGoalRunDetector.cs', 'Services/AflDroughtCalculator.cs', 'Services/AtomicFile.cs'}
    for path in sorted(SRC.rglob('*.cs')):
        rel = str(path.relative_to(SRC))
        if {'obj', 'bin'}.intersection(path.parts) or rel in native or path.name.endswith('.xaml.cs'):
            continue
        if rel.startswith('Models/') and rel not in {'Models/MarqueeMessage.cs', 'Models/SetupResult.cs'}:
            continue  # Authoritative models compile directly in Core.
        if path.name.endswith(('Qa.cs', 'Checks.cs', 'StressSuite.cs')) and path.name != 'CricketReliabilityChecks.cs':
            continue
        translate_support(rel)
    for resource in sorted((SRC / 'DesignSystem').glob('*.xaml')):
        translate_markup('DesignSystem/' + resource.stem)
    for resource in sorted((SRC / 'Resources').glob('*.xaml')):
        translate_markup('Resources/' + resource.stem)
    for view in VIEWS:
        translate_code(view, translate_markup(view))
