using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace doxyxml2md
{
    class MainClass
    {
        public static void printHelp () {
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
        }
        class Compound {
            public string id;
            public string className;
            public string type;
            public string definition;
            public string name;
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
					return name.Split(new string[] { "." }, StringSplitOptions.None).LastOrDefault();					
				}
            }
            public string location;
            public int bodyStart;
            public int bodyEnd;
        }

        static Dictionary<string, Compound> compounds;

        static Compound findCompoundByName (string name)
        {
            Compound res = compounds.Values.Where(c => c.name == name).FirstOrDefault();
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

        public static void process (string input, string output){
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
                c.compKind = (Kind)Enum.Parse(typeof(Kind), comps.Attributes["kind"].Value, true);

                foreach (XmlNode xn in comps.ChildNodes)
                {
                    switch (xn.Name)
                    {
                        case "compoundname":
                            c.name = xn.InnerText.Replace("::", ".");
                            c.className = c.GetSimpleName;
                            if (c.className.Length < c.name.Length - 1)
                                c.nameSpace = c.name.Remove(c.name.Length - c.className.Length - 1);
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
                                            p.name = membAtts.InnerText;
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

                        Compound baseClass = findBaseClass(c);
                        if (baseClass != null)
                            sr.WriteLine("- [`{0}`]({0})", baseClass.className);
						sr.WriteLine("   - `{0}`", c.className);
                        foreach (string s in c.derivedComps)
                        {
                            Compound deriv = findCompoundByName(s);
                            if (deriv == null)
                                continue;
                            sr.WriteLine("      - [`{0}`]({0})", deriv.className);
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
						sr.WriteLine("| :white_large_square: | prototype | description | link");
						sr.WriteLine("| --- | --- | --- | --- |");
						foreach (Compound prop in c.members.Where(mb => mb.compKind == Kind.Function && mb.name == c.className))
						{
							sr.Write("| [[/images/method.jpg]] | `{0} {1} {2}` | _{3}_ | [:link:]({4}",
										 prop.type, prop.name?.Trim(), prop.argsstring, prop.shortDesc?.Trim(),
										prop.location);
							if (prop.bodyStart > 0)
							{
								sr.Write("#L{0}", prop.bodyStart);
								if (prop.bodyEnd > prop.bodyStart)
									sr.Write("-L{0}", prop.bodyEnd);
							}
							sr.WriteLine(") |");
						}

						sr.WriteLine("#### Properties\n");
                        sr.WriteLine("| :white_large_square: | name | description |");
                        sr.WriteLine("| --- | --- | --- |");
                        foreach (Compound cp in c.members.Where(mb => mb.compKind == Kind.Property).OrderBy(mbb => mbb.name))
                        {
                            sr.WriteLine("| [[/images/property.jpg]] | `{0}` | _{1}_ |", cp.name?.Trim(), cp.shortDesc?.Trim());
                        }
                        sr.WriteLine("#### Methods\n");
						sr.WriteLine("| :white_large_square: | prototype | description | link");
						sr.WriteLine("| --- | --- | --- | --- |");
                        foreach (Compound cp in c.members.Where(mb => mb.compKind == Kind.Function && mb.name != c.className).OrderBy(mbb => mbb.name))
                        {
                            sr.Write("| [[/images/method.jpg]] | `{0} {1}{2}` | _{3}_ | [:link:]({4}",
                                         cp.type, cp.name?.Trim(), cp.argsstring, cp.shortDesc?.Trim(),
                                        cp.location);
                            if (cp.bodyStart > 0)
                            {
                                sr.Write("#L{0}", cp.bodyStart);
                                if (cp.bodyEnd > cp.bodyStart)
                                    sr.Write("-L{0}", cp.bodyEnd);
                            }
                            sr.WriteLine(") |");
						}
						sr.WriteLine("#### Events\n");
						sr.WriteLine("| :white_large_square: | name | description |");
						sr.WriteLine("| --- | --- | --- |");
                        foreach (Compound cp in c.members.Where(mb => mb.compKind == Kind.Event).OrderBy(mbb => mbb.name))
						{
							sr.WriteLine("| [[/images/event.jpg]] | `{0}` | _{1}_ |", cp.name?.Trim(), cp.shortDesc?.Trim());
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
				}

                process(input,output);

            }
        }
    }
}
