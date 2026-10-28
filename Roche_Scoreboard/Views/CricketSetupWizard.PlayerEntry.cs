using System.Windows;
using System.Windows.Controls;
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
    private void InitializePlayerEntry()
    {
        Loaded+=(_,_)=>InstallPlayerEntry();
        Unloaded+=(_,_)=>{++_playerEntryGeneration;if(Content is Grid host)CricketPrompt.CancelAll(host);};
        IsVisibleChanged+=(_,args)=>{if(args.NewValue is false){++_playerEntryGeneration;if(Content is Grid host)CricketPrompt.CancelAll(host);}};
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
            add.SetResourceReference(StyleProperty,"PrimaryCommandButton");System.Windows.Automation.AutomationProperties.SetName(add,home?"Add home player":"Add away player");add.Click+=(_,_)=>OpenPlayerEntry(home,add);form.Children.Insert(index,add);
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
                var label=new TextBlock{Text=text,FontSize=14,Tag="new-player-label"};
                label.SetResourceReference(TextBlock.ForegroundProperty,"ControlCentre.TextBrush");return label;
            }
            body.Children.Add(Label("First name"));body.Children.Add(first);body.Children.Add(Label("Last name"));body.Children.Add(last);body.Children.Add(Label("Roles"));
            var roles=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Margin=new Thickness(0,8,0,0)};body.Children.Add(roles);
            CheckBox Role(string label,string tag)
            {var role=new CheckBox{Content=label,ContentTemplate=CricketPrompt.ButtonContent,Tag=tag,MinHeight=58,Margin=new Thickness(3),Padding=new Thickness(14)};role.SetResourceReference(StyleProperty,"SettingsRole");roles.Children.Add(role);return role;}
            var captain=Role("Captain","new-player-captain");var keeper=Role("Wicketkeeper","new-player-keeper");
            first.Text=existing?.FirstName??"";last.Text=existing?.LastName??"";
            captain.IsChecked=existing?.IsCaptain??false;keeper.IsChecked=existing?.IsWicketkeeper??false;
            var pending=CricketPrompt.ShowAsync(host,existing is null?"Add player":"Edit player",home?SetupHomeNameBox.Text:SetupAwayNameBox.Text,body,new("close","Cancel"),new("apply",existing is null?"Add player":"Save player",true));
            var apply=PlayerEntryElements(host).OfType<Button>().LastOrDefault(button=>Equals(button.Tag,"apply"));
            void Validate(){if(apply is not null)apply.IsEnabled=!string.IsNullOrWhiteSpace(first.Text)||!string.IsNullOrWhiteSpace(last.Text);}
            first.TextChanged+=(_,_)=>Validate();last.TextChanged+=(_,_)=>Validate();Validate();
            string? result=await pending;
            if(result!="apply" || generation!=_playerEntryGeneration || !IsVisible)return;
            var players=home?_homePlayers:_awayPlayers;
            if(existing is not null && !players.Contains(existing))return;
            if(captain.IsChecked==true)foreach(var player in players)player.IsCaptain=false;
            var edited=existing??new CricketPlayer();
            edited.FirstName=first.Text.Trim();edited.LastName=last.Text.Trim();edited.IsCaptain=captain.IsChecked==true;edited.IsWicketkeeper=keeper.IsChecked==true;
            if(existing is null)players.Add(edited);
            source.Focus();
        }
        finally{_addingPlayer=false;}
    }
    private static IEnumerable<DependencyObject> PlayerEntryElements(DependencyObject root)
    {foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()){yield return child;foreach(var nested in PlayerEntryElements(child))yield return nested;}}
}
