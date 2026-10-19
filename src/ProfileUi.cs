using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Switch2Bridge {
    internal sealed class ActionChoice {
        public XboxAction Action { get;set; }
        public string Label { get;set; }
    }
    internal sealed partial class AppController {
        readonly XboxOutput output=new XboxOutput(delegate {return new VigemBackend();});
        ProfileStore profileStore;List<Profile> profiles;Profile activeProfile;
        bool fillingProfile,profileDirty;double changedAt;volatile string outputStatus="Saída Xbox parada";
        readonly Dictionary<string,ComboBox> mappingChoices=new Dictionary<string,ComboBox>();
        readonly Dictionary<string,Border> mappingRows=new Dictionary<string,Border>();
        XboxReport displayedMapping;bool haveDisplayedMapping,displayedFresh,displayedDemo;uint displayedButtons;
        void InitializeProfiles() {
            profileStore=new ProfileStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles"));
            profiles=profileStore.LoadAll(Log);string selected=profileStore.ActiveId();
            activeProfile=profiles.FirstOrDefault(p=>p.Id==selected)??profiles[0];
            var choices=Enum.GetValues(typeof(XboxAction)).Cast<XboxAction>().Select(a=>new ActionChoice{Action=a,Label=ActionLabel(a)}).ToArray();
            foreach(string source in ControllerState.Names) {
                var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(95)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
                row.Children.Add(new TextBlock {Text=Label(source),FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
                var combo=new ComboBox {ItemsSource=choices,DisplayMemberPath="Label",SelectedValuePath="Action",Tag=source,MinWidth=140,Padding=new Thickness(8,5,8,5)};
                Grid.SetColumn(combo,1);row.Children.Add(combo);
                var border=new Border {Child=row,Background=Paint.Brush("#20252C"),Padding=new Thickness(12,8,12,8),Margin=new Thickness(4),CornerRadius=new CornerRadius(6),BorderThickness=new Thickness(1),BorderBrush=Paint.Brush("#353C46")};
                Find<System.Windows.Controls.Primitives.UniformGrid>("MappingGrid").Children.Add(border);mappingChoices[source]=combo;mappingRows[source]=border;
                combo.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs e) {
                    if(fillingProfile)return;var c=(ComboBox)sender;if(c.SelectedValue==null)return;
                    activeProfile.Bindings.First(b=>b.Source==(string)c.Tag).Target=(XboxAction)c.SelectedValue;MarkProfileDirty();
                };
            }
            Find<ComboBox>("ProfilePicker").ItemsSource=profiles;
            Find<ComboBox>("ProfilePicker").SelectionChanged+=delegate {
                if(fillingProfile)return;var selectedProfile=Find<ComboBox>("ProfilePicker").SelectedItem as Profile;if(selectedProfile==null||selectedProfile==activeProfile)return;
                try {SaveCurrentProfile();ApplyProfile(selectedProfile);}catch(Exception e){ShowProfileError(e);fillingProfile=true;Find<ComboBox>("ProfilePicker").SelectedItem=activeProfile;fillingProfile=false;}
            };
            Find<TextBox>("ProfileName").TextChanged+=delegate{if(!fillingProfile)MarkProfileDirty();};
            Find<Slider>("Zone").ValueChanged+=delegate {if(!fillingProfile){activeProfile.Deadzone=Find<Slider>("Zone").Value/100;MarkProfileDirty();}};
            foreach(string name in new[]{"InvertLX","InvertLY","InvertRX","InvertRY","SwapSticks"}) {
                Find<CheckBox>(name).Checked+=AxesChanged;Find<CheckBox>(name).Unchecked+=AxesChanged;
            }
            Button("SaveProfile",delegate{try{SaveCurrentProfile();}catch(Exception e){ShowProfileError(e);}});
            Button("CopyProfile",delegate {try{SaveCurrentProfile();var p=activeProfile.Clone();p.Id=Guid.NewGuid().ToString("N");p.Name=(p.Name.Length>66?p.Name.Substring(0,66):p.Name)+" (cópia)";profileStore.Save(p);profiles.Add(p);RefreshPicker();ApplyProfile(p);}catch(Exception e){ShowProfileError(e);}});
            Button("PresetXbox",delegate{ApplyPreset(false);});Button("PresetNintendo",delegate{ApplyPreset(true);});
            Button("ImportProfile",ImportProfile);Button("ExportProfile",ExportProfile);
            Button("ToggleOutput",delegate {
                if(output.Running){output.Stop();outputStatus="Saída Xbox parada";return;}
                if(demo){outputStatus="Saia da demonstração antes de iniciar a saída Xbox.";return;}
                try{SaveCurrentProfile();output.Start();}catch(Exception e){ShowProfileError(e);}
            });
            Button("DriverPage",delegate { try {Process.Start(new ProcessStartInfo("https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0"){UseShellExecute=true});}catch(Exception e){Log(e.Message);} });
            output.StatusChanged+=delegate(string s){outputStatus=s;Log("Xbox: "+s);};
            output.Diagnostic+=delegate(string s){Log("ViGEmBus: "+s);};
            ApplyProfile(activeProfile);
        }
        static string ActionLabel(XboxAction a) {
            switch(a){case XboxAction.None:return "Sem ação";case XboxAction.Up:return "Direcional ↑";case XboxAction.Down:return "Direcional ↓";case XboxAction.Left:return "Direcional ←";case XboxAction.Right:return "Direcional →";default:return "Xbox "+a;}
        }
        void AxesChanged(object sender,RoutedEventArgs e) {
            if(fillingProfile)return;
            activeProfile.InvertLX=Find<CheckBox>("InvertLX").IsChecked==true;activeProfile.InvertLY=Find<CheckBox>("InvertLY").IsChecked==true;activeProfile.InvertRX=Find<CheckBox>("InvertRX").IsChecked==true;activeProfile.InvertRY=Find<CheckBox>("InvertRY").IsChecked==true;activeProfile.SwapSticks=Find<CheckBox>("SwapSticks").IsChecked==true;MarkProfileDirty();
        }
        void PublishMappingPlan(){var compiled=new MappingPlan(activeProfile);lock(gate){mappingPlan=compiled;output.Neutral();}forceVisual=true;}
        void MarkProfileDirty(){PublishMappingPlan();profileDirty=true;changedAt=clock.Elapsed.TotalSeconds;Find<TextBlock>("ProfileSaveStatus").Text="Alterações pendentes…";}
        void RefreshPicker(){fillingProfile=true;Find<ComboBox>("ProfilePicker").ItemsSource=null;Find<ComboBox>("ProfilePicker").ItemsSource=profiles;fillingProfile=false;}
        void ApplyProfile(Profile p) {
            fillingProfile=true;
            try{
                activeProfile=p;centers=new[]{p.CenterLX,p.CenterLY,p.CenterRX,p.CenterRY};PublishMappingPlan();
                Find<ComboBox>("ProfilePicker").SelectedItem=p;Find<TextBox>("ProfileName").Text=p.Name;Find<Slider>("Zone").Value=p.Deadzone*100;
                Find<CheckBox>("InvertLX").IsChecked=p.InvertLX;Find<CheckBox>("InvertLY").IsChecked=p.InvertLY;Find<CheckBox>("InvertRX").IsChecked=p.InvertRX;Find<CheckBox>("InvertRY").IsChecked=p.InvertRY;Find<CheckBox>("SwapSticks").IsChecked=p.SwapSticks;
                foreach(var b in p.Bindings)mappingChoices[b.Source].SelectedValue=b.Target;
                profileStore.SetActive(p);profileDirty=false;Find<TextBlock>("ProfileSaveStatus").Text="Perfil salvo · "+p.Name;
                Log("Perfil ativo: "+p.Name);
            }finally{fillingProfile=false;}
        }
        void SaveCurrentProfile() {
            if(activeProfile==null||!profileDirty)return;
            Profile snapshot=activeProfile.Clone();snapshot.Name=Find<TextBox>("ProfileName").Text.Trim();
            snapshot.Validate();profileStore.Save(snapshot);profileStore.SetActive(snapshot);
            activeProfile.Name=snapshot.Name;
            profileDirty=false;Find<TextBlock>("ProfileSaveStatus").Text="Salvo · "+DateTime.Now.ToString("HH:mm:ss");
            fillingProfile=true;Find<ComboBox>("ProfilePicker").Items.Refresh();fillingProfile=false;
        }
        void ApplyPreset(bool letters) {
            activeProfile.Bindings=Profile.Default(letters).Bindings;
            fillingProfile=true;foreach(var b in activeProfile.Bindings)mappingChoices[b.Source].SelectedValue=b.Target;fillingProfile=false;MarkProfileDirty();
        }
        void ShowProfileError(Exception e){Find<TextBlock>("ProfileSaveStatus").Text="Não salvo: "+e.Message;Log("Perfil: "+e.Message);}
        void ImportProfile(){
            var dialog=new OpenFileDialog{Filter="Perfil Switch2Bridge|*.xml",Title="Importar perfil"};if(dialog.ShowDialog(window)!=true)return;
            try{var p=ProfileStore.Read(dialog.FileName);p.Id=Guid.NewGuid().ToString("N");SaveCurrentProfile();profileStore.Save(p);profiles.Add(p);RefreshPicker();ApplyProfile(p);}catch(Exception e){ShowProfileError(e);}
        }
        void ExportProfile(){
            var dialog=new SaveFileDialog{Filter="Perfil Switch2Bridge|*.xml",FileName="Switch2Bridge-perfil.xml",Title="Exportar perfil"};if(dialog.ShowDialog(window)!=true)return;
            try{SaveCurrentProfile();ProfileStore.Write(dialog.FileName,activeProfile.Clone());Log("Perfil exportado localmente.");}catch(Exception e){ShowProfileError(e);}
        }
        void UpdateProfileUi(ControllerState s,bool fresh) {
            Find<TextBlock>("ActiveProfileLabel").Text=activeProfile.Name;
            Find<TextBlock>("OutputStatus").Text=outputStatus;
            Find<TextBlock>("OutputBadge").Text=output.Active?"●  Xbox ativo":"○  Xbox parado";Find<TextBlock>("OutputBadge").Foreground=output.Active?Paint.Green:Paint.Muted;
            Find<Button>("ToggleOutput").Content=output.Running?"Parar saída Xbox":"Iniciar saída Xbox";
            if(Find<StackPanel>("ProfilesPage").Visibility==Visibility.Visible)foreach(string b in ControllerState.Names)mappingRows[b].BorderBrush=s.Buttons.Contains(b)?Paint.Green:Paint.Brush("#353C46");
            if(Find<StackPanel>("OutputPage").Visibility==Visibility.Visible){
                var mapped=mappingPlan.Map(fresh?s:null);
                if(forceVisual||!haveDisplayedMapping||!displayedMapping.Equals(mapped))Find<TextBlock>("MappedReport").Text=mapped.ToString();
                if(forceVisual||!haveDisplayedMapping||displayedButtons!=s.Buttons.Bits||displayedFresh!=fresh||displayedDemo!=demo)Find<TextBlock>("MappedSource").Text=demo?"Prévia simulada · não enviada ao Windows":fresh?"Entrada física: "+(s.Buttons.Count==0?"nenhum botão":string.Join(" + ",s.Buttons)):"Sem dados físicos recentes · saída neutra";
                displayedMapping=mapped;haveDisplayedMapping=true;displayedButtons=s.Buttons.Bits;displayedFresh=fresh;displayedDemo=demo;
                Find<TextBlock>("PerformanceMetrics").Text=performanceText;
            }
        }
        void SaveProfileIfDue(){
            if(profileDirty&&clock.Elapsed.TotalSeconds-changedAt>0.8){try{SaveCurrentProfile();}catch(Exception e){Find<TextBlock>("ProfileSaveStatus").Text="Não salvo: "+e.Message;changedAt=clock.Elapsed.TotalSeconds+4;}}
        }
    }
}
