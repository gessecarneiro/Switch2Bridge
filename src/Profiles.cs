using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Switch2Bridge {
    public enum XboxAction { None, A, B, X, Y, Up, Down, Left, Right, LB, RB, LT, RT, Back, Start, L3, R3, Guide }
    public sealed class ButtonBinding {
        [XmlAttribute] public string Source { get; set; }
        [XmlAttribute] public XboxAction Target { get; set; }
    }
    public sealed class Profile {
        public int Version { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public double Deadzone { get; set; }
        public int CenterLX { get; set; } public int CenterLY { get; set; }
        public int CenterRX { get; set; } public int CenterRY { get; set; }
        public bool InvertLX { get; set; } public bool InvertLY { get; set; }
        public bool InvertRX { get; set; } public bool InvertRY { get; set; }
        public bool SwapSticks { get; set; }
        public List<ButtonBinding> Bindings { get; set; }
        public Profile() {
            Version=1; Id=Guid.NewGuid().ToString("N"); Name="Padrão Xbox"; Deadzone=0.08;
            CenterLX=CenterLY=CenterRX=CenterRY=2048;
            Bindings=new List<ButtonBinding>();
        }
        public static Profile Default(bool matchLabels) {
            var p=new Profile { Name=matchLabels?"Letras Nintendo":"Posição Xbox" };
            XboxAction[] actions={XboxAction.B,XboxAction.A,XboxAction.Y,XboxAction.X,XboxAction.Up,XboxAction.Down,XboxAction.Left,XboxAction.Right,XboxAction.LB,XboxAction.RB,XboxAction.LT,XboxAction.RT,XboxAction.Back,XboxAction.Start,XboxAction.L3,XboxAction.R3,XboxAction.Guide,XboxAction.None,XboxAction.None,XboxAction.None,XboxAction.None};
            for(int i=0;i<ControllerState.Names.Length;i++)p.Bindings.Add(new ButtonBinding { Source=ControllerState.Names[i],Target=actions[i] });
            if(matchLabels)foreach(var b in p.Bindings)if(new[]{"A","B","X","Y"}.Contains(b.Source))b.Target=(XboxAction)Enum.Parse(typeof(XboxAction),b.Source);
            return p;
        }
        public void Validate() {
            Guid id;
            if(Version!=1)throw new InvalidDataException("Versão de perfil não suportada.");
            if(!Guid.TryParseExact(Id,"N",out id))throw new InvalidDataException("Identificador de perfil inválido.");
            Name=(Name??"").Trim();if(Name.Length<1 || Name.Length>80)throw new InvalidDataException("O nome deve ter de 1 a 80 caracteres.");
            if(double.IsNaN(Deadzone)||double.IsInfinity(Deadzone)||Deadzone<0||Deadzone>0.30)throw new InvalidDataException("Zona morta fora do intervalo de 0 a 30%.");
            foreach(int c in new[]{CenterLX,CenterLY,CenterRX,CenterRY})if(c<1200||c>2900)throw new InvalidDataException("Centro do analógico inválido.");
            if(Bindings==null||Bindings.Count!=21||Bindings.Any(b=>b==null||!ControllerState.Names.Contains(b.Source)||!Enum.IsDefined(typeof(XboxAction),b.Target))||Bindings.Select(b=>b.Source).Distinct().Count()!=21)throw new InvalidDataException("O perfil deve conter um mapeamento válido para cada um dos 21 botões.");
        }
        public Profile Clone() {
            var p=(Profile)MemberwiseClone();p.Bindings=Bindings.Select(b=>new ButtonBinding { Source=b.Source,Target=b.Target }).ToList();return p;
        }
        public override string ToString() { return Name; }
    }
    public sealed class ProfileStore {
        readonly string folder;
        static readonly XmlSerializer Serializer=new XmlSerializer(typeof(Profile));
        public ProfileStore(string directory) { folder=Path.GetFullPath(directory);Directory.CreateDirectory(folder); }
        public static Profile Read(string path) {
            if(new FileInfo(path).Length>262144)throw new InvalidDataException("Arquivo de perfil muito grande.");
            using(var reader=XmlReader.Create(path,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=262144 })) {
                var p=(Profile)Serializer.Deserialize(reader);p.Validate();return p;
            }
        }
        public static void Write(string path,Profile profile) {
            profile.Validate();
            string full=Path.GetFullPath(path),temp=full+".tmp";
            try {
                using(var writer=XmlWriter.Create(temp,new XmlWriterSettings { Indent=true,Encoding=new UTF8Encoding(false) }))Serializer.Serialize(writer,profile);
                if(File.Exists(full))File.Replace(temp,full,null);else File.Move(temp,full);
            }finally { if(File.Exists(temp))File.Delete(temp); }
        }
        public void Save(Profile p) { p.Validate();Write(Path.Combine(folder,p.Id+".xml"),p); }
        public List<Profile> LoadAll(Action<string> error) {
            var result=new List<Profile>();
            foreach(string file in Directory.GetFiles(folder,"*.xml"))try { var p=Read(file);if(!result.Any(x=>x.Id==p.Id))result.Add(p); }catch(Exception e) { if(error!=null)error("Perfil ignorado (arquivo preservado): "+Path.GetFileName(file)+" · "+e.Message); }
            if(result.Count==0) { var p=Profile.Default(false);Save(p);result.Add(p); }
            return result.OrderBy(p=>p.Name).ToList();
        }
        public void SetActive(Profile p) { p.Validate();File.WriteAllText(Path.Combine(folder,"active.txt"),p.Id,Encoding.UTF8); }
        public string ActiveId() { string p=Path.Combine(folder,"active.txt");return File.Exists(p)?File.ReadAllText(p,Encoding.UTF8).Trim():null; }
    }
}
