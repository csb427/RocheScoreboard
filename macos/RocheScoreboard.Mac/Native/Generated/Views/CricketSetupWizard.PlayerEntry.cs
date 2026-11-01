// Generated from Views/CricketSetupWizard.PlayerEntry.cs; edit translator/adapters.
// Source SHA-256: cab781bf94d7606b5bab54ffa58514abc4d48d887245a73170bc5917ec98f9f5
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Button=Avalonia.Controls.Button;
using TextBox=Avalonia.Controls.TextBox;
using CheckBox=Avalonia.Controls.CheckBox;
using Border=Avalonia.Controls.Border;

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
        NativeEvents.WatchProperty(this,Avalonia.Controls.Control.IsVisibleProperty,true,(_,args)=>{if(args.NewValue is false){++_playerEntryGeneration;ClearPlayerRemovalRecovery();ClearSquadValidation();if(Content is Grid host)CricketPrompt.CancelAll(host);}});
    }
    private void InstallPlayerEntry()
    {
        if(_playerEntryInstalled)return;_playerEntryInstalled=true;
        foreach(bool home in new[]{true,false})
        {
            var field=home?HomePlayerFirstBox:AwayPlayerFirstBox;
            if(field.Parent is not Grid old || old.Parent is not StackPanel form)continue;
            int index=form.Children.IndexOf(old);old.IsVisible=false;
            if(index+1<form.Children.Count && form.Children[index+1] is Grid labels)labels.IsVisible=false;
            if(index+2<form.Children.Count && form.Children[index+2] is Border{Height:1} divider)divider.IsVisible=false;
            if(index>0 && form.Children[index-1] is TextBlock label)label.IsVisible=false;
            var add=new NativeButton{Content="Add player",ContentTemplate=CricketPrompt.ButtonContent,MinHeight=50,Margin=new Thickness(0,0,0,16),HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Center,Tag=home?"add-home-player":"add-away-player"};
            add.SetResourceReference(ThemeProperty,"PrimaryCommandButton");Avalonia.Automation.AutomationProperties.SetName(add,home?"Add home player":"Add away player");add.Click+=(_,_)=>OpenPlayerEntry(home,add);
            var validation=new NativeTextBlock{Text=(home?"Home":"Away")+" squad needs a second player. Add one, or remove the only player to use 11 numbered players.",Tag=home?"home-squad-validation":"away-squad-validation",IsVisible=false,FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
            validation.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");
            Avalonia.Automation.AutomationProperties.SetLiveSetting(validation,Avalonia.Automation.AutomationLiveSetting.Polite);
            var validationTarget=new StackPanel();validationTarget.Children.Add(validation);validationTarget.Children.Add(add);form.Children.Insert(index,validationTarget);
            if(home){_homeSquadValidation=validation;_homeSquadValidationTarget=validationTarget;}else{_awaySquadValidation=validation;_awaySquadValidationTarget=validationTarget;}
            var recoveryHost=new ContentControl{Tag=home?"home-player-removal-recovery":"away-player-removal-recovery",IsVisible=false,HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Stretch,Margin=new Thickness(0,0,0,12)};
            form.Children.Insert(index+1,recoveryHost);if(home)_homeRemovalHost=recoveryHost;else _awayRemovalHost=recoveryHost;
            form.SizeChanged+=(_,_)=>recoveryHost.MaxWidth=Math.Max(0,form.NativeWidth());
            recoveryHost.MaxWidth=Math.Max(0,form.NativeWidth());
            var teamName=home?HomePlyTeamName:AwayPlyTeamName;
            if(teamName.Parent is StackPanel title)
            {
                title.Margin=new Thickness(0);title.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;teamName.TextWrapping=TextWrapping.Wrap;teamName.Foreground=Avalonia.Media.Brushes.White;
                if(title.Parent is StackPanel parent)
                {
                    int position=parent.Children.IndexOf(title);parent.Children.Remove(title);
                    var header=new NativeBorder{CornerRadius=new CornerRadius(18),Padding=new Thickness(14),Margin=new Thickness(0,0,0,16),Child=title};parent.Children.Insert(position,header);
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
        if(_homeSquadValidation is not null)_homeSquadValidation.IsVisible=false;
        if(_awaySquadValidation is not null)_awaySquadValidation.IsVisible=false;
    }
    private bool SquadValidationCurrent(int version,int generation)=>_squadValidationRequested
        && version==_squadValidationVersion && generation==_playerEntryGeneration && IsLoaded && IsVisible;
    private void RefreshSquadValidation(int version,int generation)
    {
        if(!SquadValidationCurrent(version,generation))return;
        if(_homeSquadValidation is not null)_homeSquadValidation.IsVisible=_homePlayers.Count==1?true:false;
        if(!SquadValidationCurrent(version,generation))return;
        if(_awaySquadValidation is not null)_awaySquadValidation.IsVisible=_awayPlayers.Count==1?true:false;
    }
    private void QueueSquadValidation()
    {
        if(!_squadValidationRequested)return;
        int version=++_squadValidationVersion,generation=_playerEntryGeneration;
        // Do not mutate visuals inside collection notification dispatch: an
        // observer can reset the wizard when an affected visual is retired.
        Dispatcher.UIThread.Post(new Action(()=>RefreshSquadValidation(version,generation)),Avalonia.Threading.DispatcherPriority.Loaded);
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
        var owner=NativeWindow.GetWindow(this);var focusAtRequest=NativeKeyboard.FocusedElement;
        ShowStep(2,animate:false);RefreshSquadValidation(version,generation);UpdateLayout();
        var target=home?_homeSquadValidationTarget:_awaySquadValidationTarget;
        var add=target?.Children.OfType<Button>().FirstOrDefault();
        // Dynamic squad controls may not be Loaded until this first reveal.
        // Keep the focus request owned by the rejected submission, never by
        // whichever page happens to be visible when the dispatcher runs.
        Dispatcher.UIThread.Post(new Action(()=>
        {
            if(!SquadValidationCurrent(version,generation) || _currentStep!=2 || _addingPlayer
                || owner is not {IsVisible:true,IsActive:true} || !ReferenceEquals(owner,NativeWindow.GetWindow(this))
                || add is null || !RosterSourceIsCurrent(add))return;
            if(NativeKeyboard.FocusedElement is Control{IsLoaded:true,IsVisible:true,IsEnabled:true,Focusable:true} focused
                && focused is not Window && !ReferenceEquals(focused,focusAtRequest))return;
            add.Focus();OperatorViewport.RevealOperatorTarget(target!,()=>SquadValidationCurrent(version,generation) && _currentStep==2);
        }),Avalonia.Threading.DispatcherPriority.Loaded);
        return false;
    }
    private void RefreshRosterHeaders()
    {
        if(_homeRosterHeader is not null)_homeRosterHeader.Background=TeamVisuals.Panel((SetupHomeColorBox.Text ?? string.Empty),(SetupHomeColorBox.Text ?? string.Empty),.24);
        if(_awayRosterHeader is not null)_awayRosterHeader.Background=TeamVisuals.Panel((SetupAwayColorBox.Text ?? string.Empty),(SetupAwayColorBox.Text ?? string.Empty),.24);
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
            var first=new TextBox{MinHeight=46,Margin=new Thickness(0,6,0,16),Tag="new-player-first"};first.SetResourceReference(ThemeProperty,"SettingsInput");
            var last=new TextBox{MinHeight=46,Margin=new Thickness(0,6,0,20),Tag="new-player-last"};last.SetResourceReference(ThemeProperty,"SettingsInput");
            var body=new StackPanel();
            TextBlock Label(string text)
            {
                var label=new NativeTextBlock{Text=text,FontSize=14,TextWrapping=TextWrapping.Wrap,Tag="new-player-label"};
                label.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");return label;
            }
            var firstLabel=Label("First name");var lastLabel=Label("Last name");
            Avalonia.Automation.AutomationProperties.SetName(first,"First name");Avalonia.Automation.AutomationProperties.SetName(last,"Last name");
            Avalonia.Automation.AutomationProperties.SetLabeledBy(first,firstLabel);Avalonia.Automation.AutomationProperties.SetLabeledBy(last,lastLabel);
            foreach(var field in new[]{first,last})Avalonia.Automation.AutomationProperties.SetHelpText(field,"Enter a first name, last name, or both. At least one name is required.");
            var identity=new StackPanel();identity.Children.Add(Label(home?(SetupHomeNameBox.Text ?? string.Empty):(SetupAwayNameBox.Text ?? string.Empty)));
            var playerName=Label(existing?.DisplayName??"New squad player");playerName.FontSize=18;playerName.FontWeight=FontWeight.SemiBold;identity.Children.Add(playerName);
            var context=new NativeBorder{Tag="player-editor-context",Child=identity,Padding=new Thickness(12),CornerRadius=new CornerRadius(16),Margin=new Thickness(0,0,0,16),BorderThickness=new Thickness(1)};
            context.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush");context.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");body.Children.Add(context);
            body.Children.Add(firstLabel);body.Children.Add(first);body.Children.Add(lastLabel);body.Children.Add(last);
            var feedback=Label("");feedback.Tag="new-player-validation";feedback.FontSize=13;feedback.MinHeight=40;feedback.Margin=new Thickness(0,0,0,8);feedback.TextWrapping=TextWrapping.Wrap;
            feedback.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");Avalonia.Automation.AutomationProperties.SetLiveSetting(feedback,Avalonia.Automation.AutomationLiveSetting.Polite);
            body.Children.Add(feedback);body.Children.Add(Label("Roles"));
            var roles=new Avalonia.Controls.Primitives.UniformGrid{Columns=2,Margin=new Thickness(0,8,0,0)};body.Children.Add(roles);
            CheckBox Role(string label,string tag)
            {var role=new CheckBox{Content=label,ContentTemplate=CricketPrompt.ButtonContent,Tag=tag,MinHeight=58,Margin=new Thickness(3),Padding=new Thickness(14)};role.SetResourceReference(ThemeProperty,"SettingsRole");roles.Children.Add(role);return role;}
            var captain=Role("Captain","new-player-captain");var keeper=Role("Wicketkeeper","new-player-keeper");
            first.Text=existing?.FirstName??"";last.Text=existing?.LastName??"";
            captain.IsChecked=existing?.IsCaptain??false;keeper.IsChecked=existing?.IsWicketkeeper??false;
            Button? apply=null;
            string action=existing is null?"Add player":"Save player";
            bool Validate(bool reveal=false)
            {
                bool valid=!string.IsNullOrWhiteSpace((first.Text ?? string.Empty))||!string.IsNullOrWhiteSpace((last.Text ?? string.Empty));
                string help=valid?"Ready to "+(existing is null?"add":"save")+" this player.":"Enter a first name or last name to enable "+action+".";
                feedback.Text=help;
                if(apply is not null){apply.IsEnabled=valid;Avalonia.Controls.ToolTip.SetTip(apply,valid?null:help);Avalonia.Automation.AutomationProperties.SetHelpText(apply,help);}
                if(reveal && !valid && generation==_playerEntryGeneration && first.IsLoaded){first.Focus();OperatorViewport.RevealValidationError(first);}
                return valid;
            }
            var pending=CricketPrompt.ShowSourceFormAsync(host,existing is null?"Add player":"Edit player",home?(SetupHomeNameBox.Text ?? string.Empty):(SetupAwayNameBox.Text ?? string.Empty),body,source,()=>Validate(true),new("close","Cancel"),new("apply",action,true));
            apply=PlayerEntryElements(host).OfType<Button>().LastOrDefault(button=>Equals(button.Tag,"apply"));
            first.TextChanged+=(_,_)=>Validate();last.TextChanged+=(_,_)=>Validate();Validate();
            string? result=await pending;
            if(result!="apply" || generation!=_playerEntryGeneration || !IsVisible)return;
            var players=home?_homePlayers:_awayPlayers;
            if(existing is not null && !players.Contains(existing))return;
            if(captain.IsChecked==true)foreach(var player in players)player.IsCaptain=false;
            var edited=existing??new CricketPlayer();
            edited.FirstName=(first.Text ?? string.Empty).Trim();edited.LastName=(last.Text ?? string.Empty).Trim();edited.IsCaptain=captain.IsChecked==true;edited.IsWicketkeeper=keeper.IsChecked==true;
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
        host.Content=null;if(PlayerRemoval(home) is null && host.Content is null)host.IsVisible=false;
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
        Dispatcher.UIThread.Post(new Action(()=>
        {
            if(PlayerRemoval(home) is not null || !ReferenceEquals(host.Content,content))return;
            host.Content=null;if(PlayerRemoval(home) is null && host.Content is null)host.IsVisible=false;
        }),Avalonia.Threading.DispatcherPriority.Loaded);
    }
    private bool PlayerRemovalScopeCurrent(int generation,Avalonia.Controls.Window? owner)
    {
        if(generation!=_playerEntryGeneration || !IsLoaded || !IsVisible || Step3Panel.IsVisible!=true
            || owner is not {IsVisible:true} || !ReferenceEquals(owner,NativeWindow.GetWindow(this)))return false;
        for(AvaloniaObject? node=this;node is not null;node=VisualTreeHelper.GetParent(node))
            if(node is Control element && (!element.IsVisible || !element.IsEnabled || !element.IsHitTestVisible))return false;
        return true;
    }
    private bool RosterSourceIsCurrent(Control source)
    {
        if(!IsLoaded || !IsVisible || _addingPlayer || Step3Panel.IsVisible!=true
            || !source.IsLoaded || !this.IsAncestorOf(source) || NativeWindow.GetWindow(this) is not {IsVisible:true})return false;
        for(AvaloniaObject? node=source;node is not null;node=VisualTreeHelper.GetParent(node))
            if(node is Control element && (!element.IsVisible || !element.IsEnabled || !element.IsHitTestVisible))return false;
        return true;
    }
    private void RemoveSquadPlayer(bool home,Control? source)
    {
        if(source?.DataContext is not CricketPlayer player || !RosterSourceIsCurrent(source))return;
        var players=home?_homePlayers:_awayPlayers;int index=players.IndexOf(player);
        var host=home?_homeRemovalHost:_awayRemovalHost;if(index<0 || host is null)return;
        int generation=_playerEntryGeneration;var owner=NativeWindow.GetWindow(this);
        string remaining=System.Text.Json.JsonSerializer.Serialize(players.Where((_,position)=>position!=index).ToArray());
        string removed=System.Text.Json.JsonSerializer.Serialize(player);
        players.RemoveAt(index);
        if(!PlayerRemovalScopeCurrent(generation,owner) || remaining!=SquadSignature(home) || removed!=System.Text.Json.JsonSerializer.Serialize(player))return;
        var removal=new RemovedSquadPlayer(player,index,remaining,removed);
        if(home)_homeRemoval=removal;else _awayRemoval=removal;
        var body=new StackPanel();var label=new NativeTextBlock{Text="Removed "+player.FullName,TextWrapping=TextWrapping.Wrap,FontSize=14,Margin=new Thickness(0,0,0,6)};
        label.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");body.Children.Add(label);
        var undo=new NativeButton{Tag=home?"undo-home-player-removal":"undo-away-player-removal",Content="↶ Undo removal",ContentTemplate=CricketPrompt.ButtonContent,MinHeight=44,HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Center};
        undo.SetResourceReference(ThemeProperty,"ToolBtn");Avalonia.Automation.AutomationProperties.SetName(undo,"Restore player "+player.FullName);
        Avalonia.Automation.AutomationProperties.SetHelpText(undo,"Restore the latest removed player at the same position. Available until this squad changes.");
        undo.Click+=(_,_)=>RestoreRemovedSquadPlayer(home,removal,undo);body.Children.Add(undo);
        var detail=new NativeTextBlock{Text="Latest removal only · available until this squad changes",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,6,0,0)};
        detail.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.SecondaryTextBrush");body.Children.Add(detail);
        var card=new NativeBorder{Tag="squad-removal-recovery",Child=body,Padding=new Thickness(12),CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1)};
        card.SetResourceReference(Border.BackgroundProperty,"ControlCentre.SurfaceBrush");card.SetResourceReference(Border.BorderBrushProperty,"ControlCentre.BorderBrush");
        host.Content=card;
        if(!PlayerRemovalScopeCurrent(generation,owner) || !ReferenceEquals(removal,PlayerRemoval(home)) || remaining!=SquadSignature(home))return;
        host.IsVisible=true;
        if(PlayerRemovalScopeCurrent(generation,owner) && ReferenceEquals(removal,PlayerRemoval(home)) && remaining==SquadSignature(home))
            QueueSquadFocus(home,players.Count==0?null:players[Math.Min(index,players.Count-1)],source);
    }
    private void RestoreRemovedSquadPlayer(bool home,RemovedSquadPlayer removal,Button source)
    {
        if(!RosterSourceIsCurrent(source) || !ReferenceEquals(removal,PlayerRemoval(home)))return;
        int generation=_playerEntryGeneration;var owner=NativeWindow.GetWindow(this);string before=SquadSignature(home);
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
    private void QueueSquadFocus(bool home,CricketPlayer? player,Control source)
    {
        int generation=_playerEntryGeneration;string roster=SquadSignature(home);var owner=NativeWindow.GetWindow(this);
        Dispatcher.UIThread.Post(new Action(()=>
        {
            if(generation!=_playerEntryGeneration || _addingPlayer || !IsLoaded || !IsVisible || Step3Panel.IsVisible!=true
                || owner is not {IsVisible:true,IsActive:true} || !ReferenceEquals(owner,NativeWindow.GetWindow(this)) || roster!=SquadSignature(home))return;
            if(NativeKeyboard.FocusedElement is Control{IsLoaded:true,IsVisible:true,IsEnabled:true,Focusable:true} focused
                && focused is not Window && !ReferenceEquals(focused,source))return;
            string tag=(player is null?"add-":"edit-")+(home?"home-player":"away-player");
            var target=PlayerEntryVisualElements(this).OfType<Button>().FirstOrDefault(button=>Equals(button.Tag,tag)&&(player is null||ReferenceEquals(button.DataContext,player)));
            if(target is null || !RosterSourceIsCurrent(target))return;
            target.Focus();OperatorViewport.RevealOperatorTarget(target);
        }),Avalonia.Threading.DispatcherPriority.Loaded);
    }
    private static IEnumerable<AvaloniaObject> PlayerEntryVisualElements(AvaloniaObject root)
    {
        for(int index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
        {
            var child=VisualTreeHelper.GetChild(root,index);yield return child;
            foreach(var descendant in PlayerEntryVisualElements(child))yield return descendant;
        }
    }
    private static IEnumerable<AvaloniaObject> PlayerEntryElements(AvaloniaObject root)
    {foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<AvaloniaObject>()){yield return child;foreach(var nested in PlayerEntryElements(child))yield return nested;}}
}

/// <summary>One-way role availability for the squad's read-only badges.</summary>
public sealed class CricketSquadRoleVisibilityConverter : Avalonia.Data.Converters.IValueConverter
{
    public object Convert(object? value,Type targetType,object? parameter,System.Globalization.CultureInfo culture)
        => value is true ? true : false;
    public object ConvertBack(object? value,Type targetType,object? parameter,System.Globalization.CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}
