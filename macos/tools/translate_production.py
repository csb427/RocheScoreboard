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

VIEWS = ['SportSelectionScreen', 'ScorebugControl', 'CalibrationOverlay', 'BreakScreenControl', 'IntroScreenControl', 'PresentationScoreBar', 'QuarterBreakScorebugControl', 'ScorewormControl', 'StatsScreenControl', 'TrainingScorebugControl', 'CricketScorebugControl', 'CricketSummaryControl', 'WeatherScreenControl', 'MatchLoadingScreen', 'ColorPickerWindow', 'LogoCropWindow', 'TeamLibraryWindow', 'SetupWizard', 'TrainingControlPanel', 'CricketControlPanel', 'CricketSetupWizard', 'GlobalSettingsScreen', 'ScoreEventEditorWindow', 'ResumeGameDialog', 'CloudRemoteLauncher', 'ControlPanel', 'VideoPlayerControl', 'OutputScreensaverControl', 'UpdatePromptWindow', 'AflPresentationGalleryWindow', 'DesignSystemGalleryWindow', 'MainWindow']
EVENTS = {
    'MouseLeftButtonUp': 'PointerReleased', 'MouseLeftButtonDown': 'PointerPressed',
    'MouseEnter': 'PointerEntered', 'MouseLeave': 'PointerExited',
    'MouseRightButtonUp': 'PointerReleased', 'MouseRightButtonDown': 'PointerPressed',
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
    root = ET.fromstring(source.read_bytes())
    if not name.startswith('DesignSystem/'):
        root.set(f'{{{PORT}}}ProductionNames.Root', 'True')
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
            if trigger.get('Property') == 'Tag' and trigger.get('Value') == '{x:Null}':
                for setter in trigger:
                    if setter.get('Property') != 'Visibility' or setter.get('Value') != 'Collapsed':
                        raise ValueError('Tag trigger needs an explicit native mapping')
                    target_node = next(e for e in template.iter() if e.get(f'{{{X}}}Name') == setter.get('TargetName'))
                    target_node.set('Visibility', '{Binding Tag, RelativeSource={RelativeSource TemplatedParent}, Converter={x:Static conv:ObjectConverters.IsNotNull}}')
                continue
            if kind == 'DataTrigger':
                binding = trigger.get('Binding')
                for setter in trigger:
                    if setter.get('Property') != 'Visibility' or setter.get('Value') != 'Visible' or trigger.get('Value') != 'True':
                        raise ValueError(f'{name}: data trigger requires explicit native mapping')
                    target_node = next(e for e in template.iter() if e.get(f'{{{X}}}Name') == setter.get('TargetName'))
                    target_node.set('Visibility', binding)
                continue
            conditions = list(trigger.find(f'{{{WPF}}}MultiTrigger.Conditions')) if kind == 'MultiTrigger' else []
            states = []
            for condition in conditions:
                value = {'IsMouseOver': ':pointerover', 'IsSelected': ':selected', 'IsEnabled': ':disabled', 'IsKeyboardFocused': ':focus', 'IsPressed': ':pressed', 'IsChecked': ':checked'}.get(condition.get('Property'))
                if value is None:
                    raise ValueError(f'{name}: unmapped condition {condition.attrib}')
                if condition.get('Value') == 'False' and condition.get('Property') != 'IsEnabled':
                    value = ':not(' + value + ')'
                states.append(value)
            state = ''.join(states) if kind == 'MultiTrigger' else {'IsMouseOver': ':pointerover', 'IsPressed': ':pressed',
                     'IsFocused': ':focus', 'IsKeyboardFocused': ':focus', 'IsEnabled': ':disabled',
                     'IsExpanded': ':expanded', 'IsSelected': ':selected', 'IsHighlighted': ':selected', 'IsChecked': ':checked', 'IsKeyboardFocusWithin': ':focus-within'}.get(trigger.get('Property'))
            if state is None:
                raise ValueError(f'{name}: unmapped trigger {trigger.attrib}')
            if trigger.get('Property') == 'IsEnabled' and trigger.get('Value') != 'False':
                state = ':not(:disabled)'
            for setter in trigger:
                if setter.tag.endswith('}MultiTrigger.Conditions'):
                    continue
                if setter.tag.rsplit('}', 1)[-1] != 'Setter':
                    raise ValueError(f'{name}: non-setter trigger requires native animation mapping')
                target = setter.attrib.pop('TargetName', None)
                selector = f'^{state}' if is_theme else f'{owner_type}{state}'
                if target:
                    target_node = next(e for e in template.iter() if e.get(f'{{{X}}}Name') == target)
                    selector += ' /template/ ' + target_node.tag.rsplit('}', 1)[-1] + '#' + target
                style = ET.SubElement(property_node, f'{{{WPF}}}Style', {'Selector': selector})
                style.append(setter)
        if event_specs:
            template_root = next(e for e in template if not e.tag.endswith('}ControlTemplate.Triggers'))
            template_root.set('{clr-namespace:RocheScoreboard.Mac.Native}TemplateMotion.Spec', json.dumps(event_specs, separators=(',', ':')))
        template.remove(node)
    inside_template = {id(e) for t in root.iter() if t.tag.endswith('}ControlTemplate') for e in t.iter()}
    for e in root.iter():
        tag = e.tag.rsplit('}', 1)[-1]
        if e.tag.startswith('{clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF}'):
            e.tag = '{clr-namespace:RocheScoreboard.Mac.Native}NativeVideoSurface'
        if e.tag.startswith('{clr-namespace:System;assembly=mscorlib}'):
            e.tag = f'{{{X}}}{tag}'
        if tag == 'FontFamily' and e.text and name.startswith('DesignSystem/'):
            e.tag = '{clr-namespace:RocheScoreboard.Mac.Native}ProductionFontExtension'
            e.set('Declaration', e.text.strip())
            e.text = None
        if e.tag.startswith(f'{{{WPF}}}'):
            e.tag = f'{{{AV}}}{tag}'
        identifier = e.get(f'{{{X}}}Name')
        if identifier:
            names.append({'name': identifier, 'type': tag, 'object': tag in OBJECTS})
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
                e[0].set(f'{{{X}}}Name', 'PART_DecreaseButton' if 'Decrease' in tag else 'PART_IncreaseButton')
        if tag == 'Track':
            for prop in ('Minimum', 'Maximum', 'Orientation', 'IsDirectionReversed'):
                e.set(prop, '{TemplateBinding ' + prop + '}')
            e.set('Value', '{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}')
        if tag == 'ContentPresenter' and id(e) in inside_template:
            e.set('Content', e.get('Content', '{TemplateBinding Content}'))
            e.set('ContentTemplate', e.get('ContentTemplate', '{TemplateBinding ContentTemplate}'))
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
        for key, value in list(e.attrib.items()):
            local = key.rsplit('}', 1)[-1]
            if key.startswith('{') and not key.startswith((f'{{{X}}}', f'{{{PORT}}}', '{clr-namespace:RocheScoreboard.Mac.Native}')):
                del e.attrib[key]
            elif local == 'Class':
                e.set(key, 'Roche_Scoreboard.MainWindow' if name == 'MainWindow' else 'Roche_Scoreboard.Views.' + name)
            elif local in EVENTS:
                del e.attrib[key]
                wrapper = 'Native_' + value
                if EVENTS[local] in e.attrib and e.get(EVENTS[local]) != wrapper:
                    raise ValueError(f'{name}: multiple callbacks for {local} require explicit native dispatch')
                e.set(EVENTS[local], wrapper)
                event_type = {'PointerReleased': 'PointerReleasedEventArgs', 'PointerPressed': 'PointerPressedEventArgs', 'PointerEntered': 'PointerEventArgs', 'PointerExited': 'PointerEventArgs', 'PointerMoved': 'PointerEventArgs', 'PointerWheelChanged': 'PointerWheelEventArgs', 'TextInput': 'TextInputEventArgs', 'KeyDown': 'KeyEventArgs', 'SelectionChanged': 'SelectionChangedEventArgs', 'TextChanged': 'TextChangedEventArgs', 'IsCheckedChanged': 'RoutedEventArgs'}[EVENTS[local]]
                guard = 'if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Left) return; ' if local == 'MouseLeftButtonUp' else ''
                if local == 'MouseRightButtonUp':
                    guard = 'if (e.InitialPressMouseButton != Avalonia.Input.MouseButton.Right) return; '
                if local == 'MouseRightButtonDown':
                    guard = 'if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) return; '
                wrappers.append(f'    private void {wrapper}(object? sender, {event_type} e) {{ {guard}{value}(sender!, e); }}')
                events.append({'name': identifier, 'source': local, 'native': EVENTS[local], 'handler': value})
            elif local in {'TextOptions.TextFormattingMode', 'TextOptions.TextHintingMode', 'TextOptions.TextRenderingMode', 'SnapsToDevicePixels', 'LineStackingStrategy'}:
                del e.attrib[key]
                changes.append({'name': identifier, 'property': local, 'source': value})
            elif local == 'Property' and value == 'TextOptions.TextFormattingMode':
                e.set(key, 'UseLayoutRounding')
                e.set('Value', 'True')
            elif local == 'FontFamily' and not value.startswith('{'):
                e.set(key, "{fonts:ProductionFont '" + value + "'}")
                changes.append({'name': identifier, 'property': local, 'source': value, 'native_fallback': 'Inter'})
            elif local == 'Property' and value == 'FontFamily' and e.get('Value') and not e.get('Value').startswith('{'):
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
            elif local == 'Style':
                del e.attrib[key]
                e.set('Theme', value)
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
                e.set('SystemDecorations', {'None': 'None', 'ToolWindow': 'BorderOnly'}.get(value, 'Full'))
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
        if n['name'] in template_names or not n['object']:
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

def translate_code(name, fields):
    source = SRC / 'MainWindow.xaml.cs' if name == 'MainWindow' else SRC / 'Views' / f'{name}.xaml.cs'
    code = source.read_text(encoding='utf-8-sig')
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
    code = code.replace('InitializeComponent();', 'NativePointer.EnsureInitialized(); InitializeComponent(); ProductionNames.CompleteLoad(this);')
    init = ''
    if name == 'MainWindow':
        code = native_main_window(code)
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
        return re.sub(r'(?<![\w.])((?:this\.)?\w+\.Text)\b(?!\s*(?:=(?!=)|\+=|\?\?=))',
                      lambda m: m.group() if m.group() in {'System.Text', 'WebSocketMessageType.Text'} else '(' + m.group() + ' ?? string.Empty)', segment)
    parts, offset = [], 0
    for match in tokens.finditer(code):
        parts.extend((reads(code[offset:match.start()]), match.group()))
        offset = match.end()
    parts.append(reads(code[offset:]))
    return ''.join(parts).replace('SearchBox?.Text.Trim()', '(SearchBox?.Text ?? string.Empty).Trim()')

def map_native_api(code):
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
    code = code.replace('Window.GetWindow(', 'NativeWindow.GetWindow(')
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
    return code

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
    return code

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
    return code

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
    return code

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
    code = code.replace('group.Children.Add(existing);', 'group.Children.Add(existing as Transform ?? new MatrixTransform(existing.Value));')
    code = code.replace('element.Cursor == new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)', 'element.Cursor?.ToString() == "Hand"')
    return 'using Avalonia.Controls;\nusing Avalonia.Interactivity;\n' + code

def native_logo_conversion(code):
    a = code.index('        RenderTargetBitmap rendered =')
    b = code.index('        bool hasTransparency', a)
    code = code[:a] + '        int stride = width * 4;\n        byte[] pixels = NativeRaster.RenderPixels(image, width, height);\n\n' + code[b:]
    a = code.index('        PngBitmapEncoder encoder =')
    b = code.index('    }', a)
    code = code[:a] + '        using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);\n        result.Save(output);\n' + code[b:]
    code = code.replace('image.Width', 'image.Size.Width').replace('image.Height', 'image.Size.Height')
    return code

def translate_support(relative):
    path = SRC / relative
    code = path.read_text(encoding='utf-8-sig')
    for old, new in REPLACEMENTS.items():
        code = code.replace(old, new)
    code = code.replace('Avalonia.Media.PixelFormats', 'PixelFormats')
    code = code.replace('Brush = Avalonia.Media.Brush;', 'Brush = Avalonia.Media.IBrush;')
    code = re.sub(r'\bAvalonia.Media.Brush\b', 'Avalonia.Media.IBrush', code)
    code = map_native_api(code)
    if relative.startswith('MainWindow.'):
        code = native_main_window(code)
        code = 'using Avalonia.Controls;\nusing Avalonia.Media;\nusing Avalonia.Layout;\nusing Avalonia.Input;\nusing Avalonia.Interactivity;\nusing Avalonia.Threading;\nusing Avalonia.Controls.Primitives;\n' + code
    if relative == 'Views/EmergencyVideoWindow.cs':
        code = code.replace('EnableHardwareDecoding = true', 'EnableHardwareDecoding = false')
    if relative == 'Services/VlcRuntime.cs':
        code = code.replace('Core.Initialize();', 'MacVlcRuntime.Initialize();')
    if relative == 'Services/AutoUpdateService.cs':
        code = native_auto_update(code)
    if relative == 'Services/InteractionMotion.cs':
        code = native_interaction_motion(code)
    if relative == 'Services/WindowLayoutStorage.cs':
        code = re.sub(r'\bWindow\b', 'NativeWindow', code).replace('!rb.IsEmpty', 'rb.Width > 0 && rb.Height > 0')
    if relative == 'Views/EmergencyVideoWindow.cs':
        code = code.replace('WindowStyle = WindowStyle.None;', 'SystemDecorations = SystemDecorations.Full;').replace('ResizeMode = ResizeMode.NoResize;', 'CanResize = false;').replace('AllowsTransparency = false;', '')
        code = code.replace('new VideoView { Background = Brushes.Black }', 'new VideoView()').replace('VideoView videoView = new() { Background = Brushes.Black };', 'VideoView videoView = new();')
    if relative == 'Services/VideoOptimizer.cs':
        code = code.replace('ffmpeg.exe', 'ffmpeg')
    if relative == 'Services/TeamLogoService.cs':
        code = native_logo_conversion(code)
    code = re.sub(r'using WinForms = [^;]+;', '', code)
    code = 'using RocheScoreboard.Mac.Native;\nusing Avalonia;\nusing Avalonia.Controls;\nusing Avalonia.Threading;\n' + code
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
    for support in ['Models/MarqueeMessage.cs', 'Services/WeatherService.cs', 'Services/SmartContrast.cs', 'Services/ContrastHelper.cs', 'Services/FinalsPalette.cs', 'Views/ScoreboardScaleHelper.cs', 'Services/RecentColorStorage.cs', 'Services/TeamLogoService.cs', 'Services/VideoOptimizer.cs', 'Services/PresetStorage.cs', 'Services/UxPreferences.cs', 'Services/OperatorWorkflowPreferences.cs', 'Services/TeamCatalogService.cs', 'Services/RecentTrainingTimerStorage.cs', 'Services/MatchStorage.cs', 'Services/GameStateService.cs', 'Services/OverlayScheduler.cs', 'Services/DisplaySettings.cs', 'Services/QuarterLabelService.cs', 'Services/InteractionMotion.cs', 'Services/WindowLayoutStorage.cs', 'Services/AflKeybindSettings.cs', 'Services/VideoDiagnostics.cs', 'Services/VlcRuntime.cs', 'Services/AutoUpdateService.cs', 'Views/EmergencyVideoWindow.cs', 'MainWindow.RestoreMembers.cs', 'MainWindow.QuarterLabels.cs', 'MainWindow.CloudRemote.cs', 'MainWindow.CloudRemoteUi.cs', 'MainWindow.TrainingCloudRemote.cs']:
        translate_support(support)

    for source in sorted((SRC / 'Services/Cloud').glob('*.cs')):
        translate_support(str(source.relative_to(SRC)))
    for resource in ['Tokens', 'LightTheme', 'DarkTheme', 'ComfortableDensity', 'CompactDensity', 'Typography', 'Icons', 'Components']:
        translate_markup('DesignSystem/' + resource)
    for view in VIEWS:
        translate_code(view, translate_markup(view))
