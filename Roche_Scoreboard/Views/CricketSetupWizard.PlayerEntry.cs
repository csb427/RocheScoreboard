using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using CheckBox=System.Windows.Controls.CheckBox;
using Border=System.Windows.Controls.Border;

namespace Roche_Scoreboard.Views;

public partial class CricketSetupWizard
{
    private bool _playerEntryInstalled,_addingPlayer;
    private int _playerEntryGeneration;
    private Border? _homeRosterHeader,_awayRosterHeader;
    private ContentControl? _homeRemovalHost,_awayRemovalHost;
    private StackPanel? _homeSquadValidationTarget,_awaySquadValidationTarget;
    private TextBlock? _homeSquadValidation,_awaySquadValidation;
    private bool _squadValidationRequested;
    private int _squadValidationVersion;
    private sealed record RemovedSquadPlayer(CricketPlayer Player,int Index,string RemainingRoster,string RemovedPlayer);
    private RemovedSquadPlayer? _homeRemoval,_awayRemoval;
    private void InitializePlayerEntry()
    {
        Loaded+=(_,_)=>InstallPlayerEntry();
        Unloaded+=(_,_)=>{++_playerEntryGeneration;ClearPlayerRemovalRecovery();ClearSquadValidation();if(Content is Grid host)CricketPrompt.CancelAll(host);};
        IsVisibleChanged+=(_,args)=>{if(args.NewValue is false){++_playerEntryGeneration;ClearPlayerRemovalRecovery();ClearSquadValidation();if(Content is Grid host)CricketPrompt.CancelAll(host);}};
    }
    private void InstallPlayerEntry()
    {
        if(_playerEntryInstalled)return;_playerEntryInstalled=true;
        foreach(bool home in new[]{true,false})
        {
            var field=home?HomePlayerFirstBox:AwayPlayerFirstBox;
            if(field.Parent is not Grid old || old.Parent is not StackPanel form)continue;
            int index=form.Children.IndexOf(old);old.Visibility=Visibility.Collapsed;
            if(index+1<form.Children.Count && form.Children[index+1] is Grid labels)labels.Visibility=Visibility.Collapsed;
            if(index+2<form.Children.Count && form.Children[index+2] is Border{Height:1} divider)divider.Visibility=Visibility.Collapsed;
            if(index>0 && form.Children[index-1] is TextBlock label)label.Visibility=Visibility.Collapsed;
            var add=new Button{Content="Add player",ContentTemplate=CricketPrompt.ButtonContent,MinHeight=50,Margin=new Thickness(0,0,0,16),HorizontalContentAlignment=System.Windows.HorizontalAlignment.Center,Tag=home?"add-home-player":"add-away-player"};
            add.SetResourceReference(StyleProperty,"PrimaryCommandButton");System.Windows.Automation.AutomationProperties.SetName(add,home?"Add home player":"Add away player");add.Click+=(_,_)=>OpenPlayerEntry(home,add);
            var validation=new TextBlock{Text=(home?"Home":"Away")+" squad needs a second player. Add one, or remove the only player to use 11 numbered players.",Tag=home?"home-squad-validation":"away-squad-validation",Visibility=Visibility.Collapsed,FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
            validation.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");
            System.Windows.Automation.AutomationProperties.SetLiveSetting(validation,System.Windows.Automation.AutomationLiveSetting.Polite);
            var validationTarget=new StackPanel();validationTarget.Children.Add(validation);validationTarget.Children.Add(add);form.Children.Insert(index,validationTarget);
            if(home){_homeSquadValidation=validation;_homeSquadValidationTarget=validationTarget;}else{_awaySquadValidation=validation;_awaySquadValidationTarget=validationTarget;}
            var recoveryHost=new ContentControl{Tag=home?"home-player-removal-recovery":"away-player-removal-recovery",Visibility=Visibility.Collapsed,HorizontalContentAlignment=System.Windows.HorizontalAlignment.Stretch,Margin=new Thickness(0,0,0,12)};
            form.Children.Insert(index+1,recoveryHost);if(home)_homeRemovalHost=recoveryHost;else _awayRemovalHost=recoveryHost;
            form.SizeChanged+=(_,_)=>recoveryHost.MaxWidth=Math.Max(0,form.ActualWidth);
            recoveryHost.MaxWidth=Math.Max(0,form.ActualWidth);
            var teamName=home?HomePlyTeamName:AwayPlyTeamName;
            if(teamName.Parent is StackPanel title)
            {
                title.Margin=new Thickness(0);title.HorizontalAlignment=System.Windows.HorizontalAlignment.Stretch;teamName.TextWrapping=TextWrapping.Wrap;teamName.Foreground=System.Windows.Media.Brushes.White;
                if(title.Parent is StackPanel parent)
                {
                    int position=parent.Children.IndexOf(title);parent.Children.Remove(title);
                    var header=new Border{CornerRadius=new CornerRadius(18),Padding=new Thickness(14),Margin=new Thickness(0,0,0,16),Child=title};parent.Children.Insert(position,header);
                    if(home)_homeRosterHeader=header;else _awayRosterHeader=header;
                }
            }
        }
        RefreshRosterHeaders();
        _homePlayers.CollectionChanged+=(_,_)=>{RetireChangedPlayerRemoval(true);QueueSquadValidation();};
        _awayPlayers.CollectionChanged+=(_,_)=>{RetireChangedPlayerRemoval(false);QueueSquadValidation();};
    }
    private void ClearSquadValidation()
    {
        _squadValidationRequested=false;++_squadValidationVersion;
        if(_homeSquadValidation is not null)_homeSquadValidation.Visibility=Visibility.Collapsed;
        if(_awaySquadValidation is not null)_awaySquadValidation.Visibility=Visibility.Collapsed;
    }
    private bool SquadValidationCurrent(int version,int generation)=>_squadValidationRequested
        && version==_squadValidationVersion && generation==_playerEntryGeneration && IsLoaded && IsVisible;
    private void RefreshSquadValidation(int version,int generation)
    {
        if(!SquadValidationCurrent(version,generation))return;
        if(_homeSquadValidation is not null)_homeSquadValidation.Visibility=_homePlayers.Count==1?Visibility.Visible:Visibility.Collapsed;
        if(!SquadValidationCurrent(version,generation))return;
        if(_awaySquadValidation is not null)_awaySquadValidation.Visibility=_awayPlayers.Count==1?Visibility.Visible:Visibility.Collapsed;
    }
    private void QueueSquadValidation()
    {
        if(!_squadValidationRequested)return;
        int version=++_squadValidationVersion,generation=_playerEntryGeneration;
        // Do not mutate visuals inside collection notification dispatch: an
        // observer can reset the wizard when an affected visual is retired.
        Dispatcher.BeginInvoke(new Action(()=>RefreshSquadValidation(version,generation)),System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private bool ValidateSquadEntries()
    {
        bool home=_homePlayers.Count==1,away=_awayPlayers.Count==1;
        if(!home && !away)
        {
            int currentGeneration=_playerEntryGeneration;ClearSquadValidation();
            return currentGeneration==_playerEntryGeneration;
        }
        _squadValidationRequested=true;int version=++_squadValidationVersion,generation=_playerEntryGeneration;
        var owner=Window.GetWindow(this);var focusAtRequest=System.Windows.Input.Keyboard.FocusedElement;
        ShowStep(2,animate:false);RefreshSquadValidation(version,generation);UpdateLayout();
        var target=home?_homeSquadValidationTarget:_awaySquadValidationTarget;
        var add=target?.Children.OfType<Button>().FirstOrDefault();
        // Dynamic squad controls may not be Loaded until this first reveal.
        // Keep the focus request owned by the rejected submission, never by
        // whichever page happens to be visible when the dispatcher runs.
        Dispatcher.BeginInvoke(new Action(()=>
        {
            if(!SquadValidationCurrent(version,generation) || _currentStep!=2 || _addingPlayer
                || owner is not {IsVisible:true,IsActive:true} || !ReferenceEquals(owner,Window.GetWindow(this))
                || add is null || !RosterSourceIsCurrent(add))return;
            if(System.Windows.Input.Keyboard.FocusedElement is FrameworkElement{IsLoaded:true,IsVisible:true,IsEnabled:true,Focusable:true} focused
                && focused is not Window && !ReferenceEquals(focused,focusAtRequest))return;
            add.Focus();OperatorViewport.RevealOperatorTarget(target!,()=>SquadValidationCurrent(version,generation) && _currentStep==2);
        }),System.Windows.Threading.DispatcherPriority.Loaded);
        return false;
    }
    private void RefreshRosterHeaders()
    {
        if(_homeRosterHeader is not null)_homeRosterHeader.Background=TeamVisuals.Panel(SetupHomeColorBox.Text,SetupHomeColorBox.Text,.24);
        if(_awayRosterHeader is not null)_awayRosterHeader.Background=TeamVisuals.Panel(SetupAwayColorBox.Text,SetupAwayColorBox.Text,.24);
    }
    private void OpenPlayerEntry(bool home,Button source)=>OpenPlayerEditor(home,source,null);
    private void EditHomePlayer_Click(object sender,RoutedEventArgs args)
    {if(sender is Button {DataContext:CricketPlayer player} button)OpenPlayerEditor(true,button,player);}
    private void EditAwayPlayer_Click(object sender,RoutedEventArgs args)
    {if(sender is Button {DataContext:CricketPlayer player} button)OpenPlayerEditor(false,button,player);}
    private async void OpenPlayerEditor(bool home,Button source,CricketPlayer? existing)
    {
        if(_addingPlayer || Content is not Grid host)return;
        _addingPlayer=true;int generation=++_playerEntryGeneration;
        try
        {
            var first=new TextBox{MinHeight=46,Margin=new Thickness(0,6,0,16),Tag="new-player-first"};first.SetResourceReference(StyleProperty,"SettingsInput");
            var last=new TextBox{MinHeight=46,Margin=new Thickness(0,6,0,20),Tag="new-player-last"};last.SetResourceReference(StyleProperty,"SettingsInput");
            var body=new StackPanel();
            TextBlock Label(string text)
            {
                var label=new TextBlock{Text=text,FontSize=14,TextWrapping=TextWrapping.Wrap,Tag="new-player-label"};
                label.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");return label;
            }
            var firstLabel=Label("First name");var lastLabel=Label("Last name");
            System.Windows.Automation.AutomationProperties.SetName(first,"First name");System.Windows.Automation.AutomationProperties.SetName(last,"Last name");
            System.Windows.Automation.AutomationProperties.SetLabeledBy(first,firstLabel);System.Windows.Automation.AutomationProperties.SetLabeledBy(last,lastLabel);
            foreach(var field in new[]{first,last})System.Windows.Automation.AutomationProperties.SetHelpText(field,"Enter a first name, last name, or both. At least one name is required.");
            var identity=new StackPanel();identity.Children.Add(Label(home?SetupHomeNameBox.Text:SetupAwayNameBox.Text));
            var playerName=Label(existing?.DisplayName??"New squad player");playerName.FontSize=18;playerName.FontWeight=FontWeights.SemiBold;identity.Children.Add(playerName);
            var context=new Border{Tag="player-editor-context",Child=identity,Padding=new Thickness(12),CornerRadius=new CornerRadius(16),Margin=new Thickness(0,0,0,16),BorderThickness=new Thickness(1)};
            context.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush");context.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");body.Children.Add(context);
            body.Children.Add(firstLabel);body.Children.Add(first);body.Children.Add(lastLabel);body.Children.Add(last);
            var feedback=Label("");feedback.Tag="new-player-validation";feedback.FontSize=13;feedback.MinHeight=40;feedback.Margin=new Thickness(0,0,0,8);feedback.TextWrapping=TextWrapping.Wrap;
            feedback.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");System.Windows.Automation.AutomationProperties.SetLiveSetting(feedback,System.Windows.Automation.AutomationLiveSetting.Polite);
            body.Children.Add(feedback);body.Children.Add(Label("Roles"));
            var roles=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Margin=new Thickness(0,8,0,0)};body.Children.Add(roles);
            CheckBox Role(string label,string tag)
            {var role=new CheckBox{Content=label,ContentTemplate=CricketPrompt.ButtonContent,Tag=tag,MinHeight=58,Margin=new Thickness(3),Padding=new Thickness(14)};role.SetResourceReference(StyleProperty,"SettingsRole");roles.Children.Add(role);return role;}
            var captain=Role("Captain","new-player-captain");var keeper=Role("Wicketkeeper","new-player-keeper");
            first.Text=existing?.FirstName??"";last.Text=existing?.LastName??"";
            captain.IsChecked=existing?.IsCaptain??false;keeper.IsChecked=existing?.IsWicketkeeper??false;
            Button? apply=null;
            string action=existing is null?"Add player":"Save player";
            bool Validate(bool reveal=false)
            {
                bool valid=!string.IsNullOrWhiteSpace(first.Text)||!string.IsNullOrWhiteSpace(last.Text);
                string help=valid?"Ready to "+(existing is null?"add":"save")+" this player.":"Enter a first name or last name to enable "+action+".";
                feedback.Text=help;
                if(apply is not null){apply.IsEnabled=valid;apply.ToolTip=valid?null:help;System.Windows.Automation.AutomationProperties.SetHelpText(apply,help);}
                if(reveal && !valid && generation==_playerEntryGeneration && first.IsLoaded){first.Focus();OperatorViewport.RevealValidationError(first);}
                return valid;
            }
            var pending=CricketPrompt.ShowSourceFormAsync(host,existing is null?"Add player":"Edit player",home?SetupHomeNameBox.Text:SetupAwayNameBox.Text,body,source,()=>Validate(true),new("close","Cancel"),new("apply",action,true));
            apply=PlayerEntryElements(host).OfType<Button>().LastOrDefault(button=>Equals(button.Tag,"apply"));
            first.TextChanged+=(_,_)=>Validate();last.TextChanged+=(_,_)=>Validate();Validate();
            string? result=await pending;
            if(result!="apply" || generation!=_playerEntryGeneration || !IsVisible)return;
            var players=home?_homePlayers:_awayPlayers;
            if(existing is not null && !players.Contains(existing))return;
            if(captain.IsChecked==true)foreach(var player in players)player.IsCaptain=false;
            var edited=existing??new CricketPlayer();
            edited.FirstName=first.Text.Trim();edited.LastName=last.Text.Trim();edited.IsCaptain=captain.IsChecked==true;edited.IsWicketkeeper=keeper.IsChecked==true;
            if(existing is null)players.Add(edited);
            RetireChangedPlayerRemoval(home);
            source.Focus();
        }
        finally{_addingPlayer=false;}
    }
    private string SquadSignature(bool home)=>System.Text.Json.JsonSerializer.Serialize(home?_homePlayers:_awayPlayers);
    private RemovedSquadPlayer? PlayerRemoval(bool home)=>home?_homeRemoval:_awayRemoval;
    private void ClearPlayerRemoval(bool home)
    {
        if(home)_homeRemoval=null;else _awayRemoval=null;
        var host=home?_homeRemovalHost:_awayRemovalHost;if(host is null)return;
        host.Content=null;if(PlayerRemoval(home) is null && host.Content is null)host.Visibility=Visibility.Collapsed;
    }
    private void ClearPlayerRemovalRecovery(){ClearPlayerRemoval(true);ClearPlayerRemoval(false);}
    private void RetireChangedPlayerRemoval(bool home)
    {
        if(PlayerRemoval(home) is not { } removal || removal.RemainingRoster==SquadSignature(home)
            && removal.RemovedPlayer==System.Text.Json.JsonSerializer.Serialize(removal.Player))return;
        // Revoke acceptance immediately; retire the visual after collection
        // notifications return, so Unloaded cannot re-enter a roster mutation.
        if(home)_homeRemoval=null;else _awayRemoval=null;
        var host=home?_homeRemovalHost:_awayRemovalHost;var content=host?.Content;
        if(host is null || content is null)return;
        Dispatcher.BeginInvoke(new Action(()=>
        {
            if(PlayerRemoval(home) is not null || !ReferenceEquals(host.Content,content))return;
            host.Content=null;if(PlayerRemoval(home) is null && host.Content is null)host.Visibility=Visibility.Collapsed;
        }),System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private bool PlayerRemovalScopeCurrent(int generation,Window? owner)
    {
        if(generation!=_playerEntryGeneration || !IsLoaded || !IsVisible || Step3Panel.Visibility!=Visibility.Visible
            || owner is not {IsVisible:true} || !ReferenceEquals(owner,Window.GetWindow(this)))return false;
        for(DependencyObject? node=this;node is not null;node=VisualTreeHelper.GetParent(node))
            if(node is UIElement element && (!element.IsVisible || !element.IsEnabled || !element.IsHitTestVisible))return false;
        return true;
    }
    private bool RosterSourceIsCurrent(FrameworkElement source)
    {
        if(!IsLoaded || !IsVisible || _addingPlayer || Step3Panel.Visibility!=Visibility.Visible
            || !source.IsLoaded || !this.IsAncestorOf(source) || Window.GetWindow(this) is not {IsVisible:true})return false;
        for(DependencyObject? node=source;node is not null;node=VisualTreeHelper.GetParent(node))
            if(node is UIElement element && (!element.IsVisible || !element.IsEnabled || !element.IsHitTestVisible))return false;
        return true;
    }
    private void RemoveSquadPlayer(bool home,FrameworkElement? source)
    {
        if(source?.DataContext is not CricketPlayer player || !RosterSourceIsCurrent(source))return;
        var players=home?_homePlayers:_awayPlayers;int index=players.IndexOf(player);
        var host=home?_homeRemovalHost:_awayRemovalHost;if(index<0 || host is null)return;
        int generation=_playerEntryGeneration;var owner=Window.GetWindow(this);
        string remaining=System.Text.Json.JsonSerializer.Serialize(players.Where((_,position)=>position!=index).ToArray());
        string removed=System.Text.Json.JsonSerializer.Serialize(player);
        players.RemoveAt(index);
        if(!PlayerRemovalScopeCurrent(generation,owner) || remaining!=SquadSignature(home) || removed!=System.Text.Json.JsonSerializer.Serialize(player))return;
        var removal=new RemovedSquadPlayer(player,index,remaining,removed);
        if(home)_homeRemoval=removal;else _awayRemoval=removal;
        var body=new StackPanel();var label=new TextBlock{Text="Removed "+player.FullName,TextWrapping=TextWrapping.Wrap,FontSize=14,Margin=new Thickness(0,0,0,6)};
        label.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(label);
        var undo=new Button{Tag=home?"undo-home-player-removal":"undo-away-player-removal",Content="↶ Undo removal",ContentTemplate=CricketPrompt.ButtonContent,MinHeight=44,HorizontalContentAlignment=System.Windows.HorizontalAlignment.Center};
        undo.SetResourceReference(StyleProperty,"ToolBtn");System.Windows.Automation.AutomationProperties.SetName(undo,"Restore player "+player.FullName);
        System.Windows.Automation.AutomationProperties.SetHelpText(undo,"Restore the latest removed player at the same position. Available until this squad changes.");
        undo.Click+=(_,_)=>RestoreRemovedSquadPlayer(home,removal,undo);body.Children.Add(undo);
        var detail=new TextBlock{Text="Latest removal only · available until this squad changes",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,6,0,0)};
        detail.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");body.Children.Add(detail);
        var card=new Border{Tag="squad-removal-recovery",Child=body,Padding=new Thickness(12),CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1)};
        card.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush");card.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");
        host.Content=card;
        if(!PlayerRemovalScopeCurrent(generation,owner) || !ReferenceEquals(removal,PlayerRemoval(home)) || remaining!=SquadSignature(home))return;
        host.Visibility=Visibility.Visible;
        if(PlayerRemovalScopeCurrent(generation,owner) && ReferenceEquals(removal,PlayerRemoval(home)) && remaining==SquadSignature(home))
            QueueSquadFocus(home,players.Count==0?null:players[Math.Min(index,players.Count-1)],source);
    }
    private void RestoreRemovedSquadPlayer(bool home,RemovedSquadPlayer removal,Button source)
    {
        if(!RosterSourceIsCurrent(source) || !ReferenceEquals(removal,PlayerRemoval(home)))return;
        int generation=_playerEntryGeneration;var owner=Window.GetWindow(this);string before=SquadSignature(home);
        if(removal.RemainingRoster!=before || removal.RemovedPlayer!=System.Text.Json.JsonSerializer.Serialize(removal.Player))
        {
            ClearPlayerRemoval(home);
            if(PlayerRemovalScopeCurrent(generation,owner) && before==SquadSignature(home))QueueSquadFocus(home,(home?_homePlayers:_awayPlayers).FirstOrDefault(),source);
            return;
        }
        var players=home?_homePlayers:_awayPlayers;if(players.Contains(removal.Player))return;
        int index=Math.Clamp(removal.Index,0,players.Count);var restored=players.ToList();restored.Insert(index,removal.Player);
        string after=System.Text.Json.JsonSerializer.Serialize(restored);
        players.Insert(index,removal.Player);
        if(PlayerRemovalScopeCurrent(generation,owner) && PlayerRemoval(home) is null && after==SquadSignature(home))QueueSquadFocus(home,removal.Player,source);
    }
    private void QueueSquadFocus(bool home,CricketPlayer? player,FrameworkElement source)
    {
        int generation=_playerEntryGeneration;string roster=SquadSignature(home);var owner=Window.GetWindow(this);
        Dispatcher.BeginInvoke(new Action(()=>
        {
            if(generation!=_playerEntryGeneration || _addingPlayer || !IsLoaded || !IsVisible || Step3Panel.Visibility!=Visibility.Visible
                || owner is not {IsVisible:true,IsActive:true} || !ReferenceEquals(owner,Window.GetWindow(this)) || roster!=SquadSignature(home))return;
            if(System.Windows.Input.Keyboard.FocusedElement is FrameworkElement{IsLoaded:true,IsVisible:true,IsEnabled:true,Focusable:true} focused
                && focused is not Window && !ReferenceEquals(focused,source))return;
            string tag=(player is null?"add-":"edit-")+(home?"home-player":"away-player");
            var target=PlayerEntryVisualElements(this).OfType<Button>().FirstOrDefault(button=>Equals(button.Tag,tag)&&(player is null||ReferenceEquals(button.DataContext,player)));
            if(target is null || !RosterSourceIsCurrent(target))return;
            target.Focus();OperatorViewport.RevealOperatorTarget(target);
        }),System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private static IEnumerable<DependencyObject> PlayerEntryVisualElements(DependencyObject root)
    {
        for(int index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
        {
            var child=VisualTreeHelper.GetChild(root,index);yield return child;
            foreach(var descendant in PlayerEntryVisualElements(child))yield return descendant;
        }
    }
    private static IEnumerable<DependencyObject> PlayerEntryElements(DependencyObject root)
    {foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()){yield return child;foreach(var nested in PlayerEntryElements(child))yield return nested;}}
}

/// <summary>One-way role availability for the squad's read-only badges.</summary>
public sealed class CricketSquadRoleVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value,Type targetType,object? parameter,System.Globalization.CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value,Type targetType,object? parameter,System.Globalization.CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
