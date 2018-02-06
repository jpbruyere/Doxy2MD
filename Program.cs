using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace Doxy2MD
{
    class MainClass
    {
        public static void printHelp () {
            Console.WriteLine("doxy2md");
            Console.WriteLine("=======\n");
            Console.WriteLine("doxygen xml to markdown converter\n");
            Console.WriteLine("usage: doxyxml2md [options] inputdirectory\n\n");
            Console.WriteLine("options:\n");
            Console.WriteLine("\t-h, --help:          print this help message");
            Console.WriteLine("\t-o, --output <path>: output directory");
        }

        enum Kind {
            Class,
            Interface,
            Namespace,
            Struct,
            Enum,
            File,
            Dir,
            Property,
            Function,
            Event,
            Variable,
            Page,
        }

        class InheritanceData {
            public int id;
            public string name;
            public int ancestor;
        }
        class Compound {
            public string id;
            public string className;
            public string type;
            public string definition;
            public string FullName;
            public string nameSpace;
            public string shortDesc;
            public string longDesc;
            public Kind compKind;
            public List<string> baseComps = new List<string>();
            public List<string> derivedComps = new List<string>();
            public List<Compound> members = new List<Compound>();
            public string argsstring;

            public string GetSimpleName {
                get {
					return FullName.Split(new string[] { "." }, StringSplitOptions.None).LastOrDefault();					
				}
            }
            public string location;
            public int bodyStart;
            public int bodyEnd;
            public override string ToString()
            {
                return string.Format("[Compound: GetSimpleName={0}]", GetSimpleName);
            }
        }

        static Dictionary<string, Compound> compounds;

        static Compound findCompoundByName (string name)
        {
            Compound res = compounds.Values.Where(c => c.FullName == name).FirstOrDefault();
            if (res == null)
                Console.WriteLine("compound not found: {0}", name);
			return res;
        }

        static Compound findBaseClass (Compound c){
            foreach (string s in c.baseComps)
            {
                Compound b = findCompoundByName(s);
                if (b?.compKind == Kind.Class)
                    return b;
            }
            return null;
        }
		
        static Compound[] findIFaces(Compound c)
		{
            List<Compound> res = new List<Compound>();
			foreach (string s in c.baseComps)
			{
				Compound b = findCompoundByName(s);
                if (b?.compKind == Kind.Interface)
                    res.Add(b);
			}
			return res.ToArray();
		}

        static string processDesciption (XmlNode xn){
            string desc = "";
            foreach (XmlNode para in xn.ChildNodes)
            {
                if (para.Name != "para")
                    throw new Exception("unknown tag in description");
				foreach (XmlNode d in para.ChildNodes)
				{
                    /*if (d.NodeType == XmlNodeType.Text)
                    {
                        desc += d.Value + "\n";
                        continue;
                    }*/
                    if (d.Name == "itemizedlist"){
                        desc += "\n";
                        foreach (XmlNode item in d.ChildNodes)
                        {
                            desc += "* " + item.InnerText + "\n";
                        }
                        continue;
                    }
                    desc += d.InnerText;
                }
                desc += "\n";
            }
            //if (!string.IsNullOrEmpty(desc))
            //    System.Diagnostics.Debugger.Break();
            return desc;
        }

        static void printAncestor (StreamWriter sr, InheritanceData ih, ref string tabs){
            if (ih.ancestor >= 0)
                printAncestor(sr, inheritanceGraph[ih.ancestor], ref tabs);            
			sr.WriteLine(tabs + "- [`{0}`]({0})", ih.name);
			tabs += "  ";
		}
        static Dictionary<int, InheritanceData> inheritanceGraph;
        public static void process (string input, string output){
            inheritanceGraph = new Dictionary<int, InheritanceData>();

            compounds = new Dictionary<string, Compound>();
            Directory.CreateDirectory(output);

            string[] infiles = Directory.GetFiles(input,"*.xml");

            foreach (string infile in infiles)
            {
                Compound c = new Compound();

                XmlDocument doc = new XmlDocument();
                using (Stream ins = new FileStream(infile, FileMode.Open)){
                    doc.Load(ins);
                }

                XmlNode comps = doc.SelectSingleNode("/doxygen/compounddef");

                if (comps == null)
                    continue;
                c.id = comps.Attributes["id"].Value;
                c.compKind = (Kind)Enum.Parse(typeof(Kind), comps.Attributes["kind"]?.Value, true);

                foreach (XmlNode xn in comps.ChildNodes)
                {
                    switch (xn.Name)
                    {
                        case "compoundname":
                            c.FullName = xn.InnerText.Replace("::", ".");
                            c.className = c.GetSimpleName;
                            if (c.className.Length < c.FullName.Length - 1)
                                c.nameSpace = c.FullName.Remove(c.FullName.Length - c.className.Length - 1);
                            break;
                        case "basecompoundref":
                            c.baseComps.Add(xn.InnerText);
                            break;
						case "derivedcompoundref":
							c.derivedComps.Add(xn.InnerText);
							break;
						case "briefdescription":
							c.shortDesc = processDesciption(xn);
							break;
						case "detaileddescription":
                            c.longDesc = processDesciption(xn);
							break;
						case "inheritancegraph":                            
                            foreach (XmlNode node in xn.ChildNodes)
                            {
                                InheritanceData ih = new InheritanceData();
                                ih.id = int.Parse(node.Attributes["id"].Value);
                                ih.name = node["label"].InnerText?.Split('.').LastOrDefault();

                                if (node["childnode"] == null)
                                    ih.ancestor = -1;
                                else
                                    ih.ancestor = int.Parse(node["childnode"].Attributes["refid"].Value);

                                if (inheritanceGraph.ContainsKey(ih.id)){
                                    if (inheritanceGraph[ih.id].ancestor < 0)
                                        inheritanceGraph[ih.id].ancestor = ih.ancestor;
                                }else
                                    inheritanceGraph[ih.id] = ih;
                            }

							break;							
						case "sectiondef":                            
                            foreach (XmlNode memb in xn.ChildNodes)
                            {
                                Compound p = new Compound();
                                p.compKind = (Kind)Enum.Parse(typeof(Kind), memb.Attributes["kind"].Value, true);
                                if (memb.Attributes["prot"]?.Value != "public")
                                    continue;
                                foreach (XmlNode membAtts in memb.ChildNodes)
                                {
                                    switch (membAtts.Name)
                                    {
										case "type":
                                            p.type = membAtts.InnerText?.Split(' ').LastOrDefault();
											break;
										case "definition":
                                            p.definition = membAtts.InnerText;
											break;
										case "name":
                                            p.FullName = membAtts.InnerText;
											break;
										case "argsstring":
											p.argsstring = membAtts.InnerText;
											break;
										case "location":
                                            p.location = Path.Combine("../blob/master", membAtts.Attributes["file"].Value);
                                            int tmp = 0;
                                            if (int.TryParse(membAtts.Attributes["bodystart"]?.Value, out tmp))
                                                p.bodyStart = tmp;
											if (int.TryParse(membAtts.Attributes["bodyend"]?.Value, out tmp))
												p.bodyEnd = tmp;
											break;
										case "briefdescription":
											p.shortDesc = processDesciption(membAtts);
											break;
										case "detaileddescription":
											p.longDesc = processDesciption(membAtts);
											break;
									}
                                }
                                c.members.Add(p);
                            }
                            break;
                    }
                }
                compounds[c.id] = c;
            }

            foreach (KeyValuePair<string,Compound> kc in compounds.Where(cp => cp.Value.compKind == Kind.Class))
            {
                Compound c = kc.Value;
				using (Stream os = new FileStream(Path.Combine(output, c.className) + ".md", FileMode.Create))
				{
                    using (StreamWriter sr = new StreamWriter(os))
                    {
                        sr.WriteLine(c.shortDesc);
                        sr.WriteLine();
                        sr.WriteLine(c.longDesc);
                        sr.WriteLine();
                        sr.WriteLine("**namespace**:  `{0}`\n\n", c.nameSpace);

                        sr.WriteLine("#### Inheritance Hierarchy\n");

                        //if (c.className == "ComboBox")
                            //System.Diagnostics.Debugger.Break();
                        string tabs = "";
                        InheritanceData igThis = inheritanceGraph.Values.FirstOrDefault(nd => nd.name == c.className);
                        if (igThis?.ancestor >= 0)
                        {
                            printAncestor(sr, inheritanceGraph[igThis.ancestor], ref tabs);
                        }
                        sr.WriteLine(tabs + "- `{0}`", c.className);
						tabs += "  ";

						Compound baseClass = findBaseClass(c);

                        foreach (string s in c.derivedComps)
                        {
                            Compound deriv = findCompoundByName(s);
                            if (deriv == null)
                                continue;
                            sr.WriteLine(tabs + "- [`{0}`]({0})", deriv.className);
                        }

                        sr.WriteLine("#### Syntax\n");
                        sr.WriteLine("```csharp");
                        sr.Write("public class {0}", c.className);
                        Compound[] ifaces = findIFaces(c);
                        int ifaceIdx = 0;
                        if (baseClass != null)
                            sr.Write(" : {0}", baseClass.className);
                        else if (ifaces.Length > 0)
                        {
                            sr.Write(" : {0}", ifaces[0].className);
                            ifaceIdx = 1;
                        }
                        while (ifaceIdx < ifaces.Length){
                            sr.Write(", {0}", ifaces[ifaceIdx].className);
                            ifaceIdx++;
                        }

                        sr.WriteLine("");
                        sr.WriteLine("```");

                        sr.WriteLine("#### Constructors\n");
						sr.WriteLine("| :white_large_square: | prototype | description");
						sr.WriteLine("| --- | --- | --- |");
						foreach (Compound prop in c.members.Where(mb => mb.compKind == Kind.Function && mb.FullName == c.className))
						{
							sr.WriteLine("| [[/images/method.jpg]] | `{0} {1} {2}` | _{3}_",
										 prop.type, prop.FullName?.Trim(), prop.argsstring, prop.shortDesc?.Trim());
						}

						sr.WriteLine("#### Properties\n");
                        sr.WriteLine("| :white_large_square: | name | description |");
                        sr.WriteLine("| --- | --- | --- |");
                        foreach (Compound cp in c.members.Where(mb => mb.compKind == Kind.Property).OrderBy(mbb => mbb.FullName))
                        {
                            sr.WriteLine("| [[/images/property.jpg]] | `{0}` | _{1}_ |", cp.FullName?.Trim(), cp.shortDesc?.Trim());
                        }
                        sr.WriteLine("#### Methods\n");
						sr.WriteLine("| :white_large_square: | prototype | description |");
						sr.WriteLine("| --- | --- | --- |");
                        foreach (Compound cp in c.members.Where(mb => mb.compKind == Kind.Function && mb.FullName != c.className).OrderBy(mbb => mbb.FullName))
                        {
                            sr.WriteLine("| [[/images/method.jpg]] | `{0} {1}{2}` | _{3}_ |",
                                         cp.type, cp.FullName?.Trim(), cp.argsstring, cp.shortDesc?.Trim());                            
						}
						sr.WriteLine("#### Events\n");
						sr.WriteLine("| :white_large_square: | name | description |");
						sr.WriteLine("| --- | --- | --- |");
                        foreach (Compound cp in c.members.Where(mb => mb.compKind == Kind.Event).OrderBy(mbb => mbb.FullName))
						{
							sr.WriteLine("| [[/images/event.jpg]] | `{0}` | _{1}_ |", cp.FullName?.Trim(), cp.shortDesc?.Trim());
						}
                    }
				}

			}
			
            using (Stream os = new FileStream(Path.Combine(output, "index.md"), FileMode.Create))
			{
				using (StreamWriter sr = new StreamWriter(os))
				{
					foreach (IGrouping<string,KeyValuePair<string, Compound>> nkc in compounds.Where(cp => cp.Value.compKind == Kind.Class).GroupBy(cpp => cpp.Value.nameSpace))
					{
                        sr.WriteLine("## `{0}` namespace\n", nkc.Key);
						sr.WriteLine("| class | description |");
						sr.WriteLine("| --- | --- |");
						foreach (KeyValuePair<string, Compound> kc in nkc)
                        {
                            Compound c = kc.Value;
                            sr.WriteLine("| [`{0}`]({0}) | _{1}_ |", c.className, c.shortDesc?.Trim());
                        }
					}
				}
			}

        }


        public static void Main(string[] args)
        {
			Console.WriteLine("Doxy2MD");
			Console.WriteLine("=======\n");
            if (args.Length == 0){
                printHelp();
                return;
            }
			string output = "",
            input = "";
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.StartsWith("-"))
                {
                    switch (arg)
                    {
						case "-o":
						case "--output":
							output = args[i + 1];
							break;
						case "-h":
						case "--help":
							printHelp();
							return;
						default:
                            Console.WriteLine("Unknown option: {0}", arg);
                            printHelp();
                            return;
                    }
                    i++;
                    continue;
                }

                input = arg;
                if (!Directory.Exists(input)){
					Console.WriteLine("input path not found: {0}", input);
					printHelp();
                    return;
				}
            }
            Console.WriteLine("Processing: {0} => {1}", input, output);
			process(input, output);
		}
    }
}
