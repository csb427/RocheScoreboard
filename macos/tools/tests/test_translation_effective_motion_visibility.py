"""Motion reads WPF effective visibility while local Visibility writes stay local."""
import importlib.util
from pathlib import Path
import unittest
SPEC=importlib.util.spec_from_file_location('translator',Path(__file__).parents[1]/'translate_production.py')
translator=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(translator)
def mapped(source):
    source=translator.native_event_subscriptions(source)
    for old,new in translator.REPLACEMENTS.items():source=source.replace(old,new)
    return translator.map_native_api(source)
class EffectiveMotionVisibilityTests(unittest.TestCase):
    def test_actual_choice_owner_checks_effective_visibility_for_retarget_frame_and_observer(self):
        source=(Path(__file__).resolve().parents[3]/'Roche_Scoreboard/Services/OperatorChoiceMotion.cs').read_text(encoding='utf-8-sig')
        code=mapped(source)
        self.assertEqual(3,code.count('NativeEffectiveVisibility.IsVisible(control)'))
        self.assertNotIn('control.IsVisible',code)
        self.assertIn('NativeEffectiveVisibility.WatchProperty(control,Avalonia.Controls.Control.IsVisibleProperty,true,VisibilityChanged)',code)
        self.assertIn('NativeEffectiveVisibility.WatchProperty(control,Avalonia.Controls.Control.IsVisibleProperty,false,VisibilityChanged)',code)
        self.assertIn('{ Settle(); return; }',code)
        self.assertIn('CompositionTarget.Rendering',source)
    def test_new_control_owner_preserves_local_visibility_writes(self):
        code=mapped('class OperatorChoiceMotion { void Check(){control.Visibility = Visibility.Collapsed; if(!control.IsVisible)Settle();} }')
        self.assertIn('control.IsVisible = false;',code)
        self.assertIn('!NativeEffectiveVisibility.IsVisible(control)',code)
    def test_control_reads_effective_without_rewriting_popup_local_assignment(self):
        code=mapped('class OperatorControlMotion { void Update(){ if(!_control.IsVisible) Reset(); _popupSurface.Visibility = Visibility.Collapsed; } }')
        self.assertIn('!NativeEffectiveVisibility.IsVisible(_control)',code)
        self.assertIn('_popupSurface.IsVisible = false;',code)
    def test_choice_visibility_gate_and_observer_keep_same_owner(self):
        code=mapped('class OperatorChoiceMotion { void Check(){ if(!button.IsVisible) Settle(button); button.IsVisibleChanged += Availability; button.IsVisibleChanged -= Availability; } }')
        self.assertIn('!NativeEffectiveVisibility.IsVisible(button)',code)
        self.assertIn('NativeEffectiveVisibility.WatchProperty(button,Avalonia.Controls.Control.IsVisibleProperty,true,Availability)',code)
        self.assertIn('NativeEffectiveVisibility.WatchProperty(button,Avalonia.Controls.Control.IsVisibleProperty,false,Availability)',code)
    def test_symbol_bare_effective_read_does_not_corrupt_property_event_name(self):
        code=mapped('class OperatorSymbol { void Activate(){if(!IsLoaded || !IsVisible)return;} void Hook(){IsVisibleChanged += Changed;} }')
        self.assertIn('!NativeEffectiveVisibility.IsVisible(this)',code)
        self.assertIn('NativeEffectiveVisibility.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,Changed)',code)
    def test_actual_visibility_assignment_is_never_a_method_call_target(self):
        code=mapped('class OperatorControlMotion { void Check(){button.Visibility = Visibility.Visible; if(button.IsVisible==true)Paint();} }')
        self.assertIn('button.IsVisible = true;',code)
        self.assertIn('NativeEffectiveVisibility.IsVisible(button)==true',code)
    def test_unrelated_classes_keep_the_existing_visibility_contract(self):
        code=mapped('class Other { void Check(){if(!_control.IsVisible)return; _control.IsVisibleChanged += Changed;} }')
        self.assertIn('!_control.IsVisible',code)
        self.assertIn('NativeEvents.WatchProperty(_control,Avalonia.Controls.Control.IsVisibleProperty,true,Changed)',code)
        self.assertNotIn('NativeEffectiveVisibility',code)
    def test_scorebar_reads_and_observes_effective_visibility_without_rewriting_assignment(self):
        code=mapped('class PresentationScoreBar { bool Owns => IsLoaded && IsVisible; void Hook(){IsVisibleChanged += Changed; IsVisibleChanged -= Changed; Visibility = Visibility.Collapsed;} }')
        self.assertIn('IsLoaded && NativeEffectiveVisibility.IsVisible(this)',code)
        self.assertIn('NativeEffectiveVisibility.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,Changed)',code)
        self.assertIn('NativeEffectiveVisibility.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,false,Changed)',code)
        self.assertIn('IsVisible = false;',code)
    def test_loader_reads_and_watches_effective_ancestor_availability(self):
        code=mapped('class MatchLoadingScreen { bool CanAnimate => IsLoaded && IsVisible && IsEnabled && _owner.IsEnabled; void Hook(){IsVisibleChanged += Changed; IsEnabledChanged += Changed; IsEnabledChanged -= Changed;} }')
        self.assertIn('IsLoaded && NativeEffectiveVisibility.IsVisible(this) && IsEffectivelyEnabled && _owner.IsEffectivelyEnabled',code)
        self.assertIn('NativeEffectiveVisibility.WatchProperty(this,Avalonia.Input.InputElement.IsEffectivelyEnabledProperty,true,Changed)',code)
        self.assertIn('NativeEffectiveVisibility.WatchProperty(this,Avalonia.Input.InputElement.IsEffectivelyEnabledProperty,false,Changed)',code)
        self.assertNotIn('Control.IsEnabledProperty',code)
if __name__=='__main__':unittest.main()
