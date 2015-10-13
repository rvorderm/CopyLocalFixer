using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CopyLocalFixer
{
    internal class Program
    {
        private static void WriteUsage()
        {
            Console.WriteLine("Usage: CopyLocalFixer {/rewrite | /restore} <Path-to-SLN-file> [--root=path]");
            Console.WriteLine();
            Console.WriteLine("CopyLocalFixer analyzes solution and re-writes csproj files so that each external");
            Console.WriteLine("assembly referenced with CopyLocal (or Private which is the same) not more than once.");
            Console.WriteLine("It is needed because otherwise during parallel build copying of same file happens");
            Console.WriteLine("twice at times and results in an error about not accessible file for one of them.");
            Console.WriteLine("If the optional RelativeBuildRoot option is specified");
        }

        private static void Main(string[] args)
        {
#if DEBUG
			args = new string[] { "/rewrite", "C:\\Repos\\Default\\Personnel\\Personnel.sln", "--root=Windows\\bin\\" };
#endif
			if (2 <= args.Length || args.Length>3 || 
				args[0] == "-h" || args[0] == "/?" || args[0] == "/h" ||
                args[0] != "/rewrite" && args[0] != "/restore")
            {
                WriteUsage();
                return;
            }
	        bool rewrite = args[0] == "/rewrite";

	        var solutionPath = args[1];
	        var sln = new SlnParser().Parse(new StreamReader(solutionPath));

	        string buildPath = null;
	        if (args.Length > 2)
	        {
		        if (args[2].StartsWith("--root="))
		        {
			        buildPath = args[2].Replace("--root=", string.Empty);
		        }
	        }

	        var assemblies = new HashSet<string>();
            foreach (var csprojPath in sln.Projects.Select(p => p.Path))
            {
	            var directoryName = Path.GetDirectoryName(solutionPath);
	            var fullCsprojPath = Path.Combine(directoryName, csprojPath);
				var relativePath = MakeRelativePath(Path.GetDirectoryName(fullCsprojPath) + "\\", directoryName+"\\") + buildPath;
				if (rewrite)
                    Rewrite(fullCsprojPath, assemblies, relativePath);
                else
                    Restore(fullCsprojPath);
            }
        }

        private static void Rewrite(string fullCsprojPath, HashSet<string> assemblies, string relativePath = null)
        {
	        var writeTime = new FileInfo(fullCsprojPath).LastWriteTimeUtc;
            var doc = XDocument.Load(fullCsprojPath);
            var wasChanged = RewriteReferences(assemblies, doc);
	        if (!string.IsNullOrEmpty(relativePath))
	        {
		        wasChanged = wasChanged || RewriteOutputPaths(relativePath, doc);
	        }
	        wasChanged = wasChanged || OrmRemover.Instance.StripOrmTags(doc);
	        if (wasChanged)
            {
                StripReadonlyIfSet(fullCsprojPath);
                MakeBackUp(fullCsprojPath);
                doc.Save(fullCsprojPath, SaveOptions.None);
                new FileInfo(fullCsprojPath).LastWriteTimeUtc = writeTime;
            }
        }

	    private static void MakeBackUp(string fullCsprojPath)
	    {
		    var destFileName = fullCsprojPath + ".backup";
		    if(File.Exists(destFileName))
			{
				File.Delete(destFileName);
			}
		    File.Move(fullCsprojPath, destFileName);
	    }


	    private static bool RewriteOutputPaths(string relativePath, XDocument doc)
	    {
		    bool wasChanged = false;
			var items = from itemGroupElement in doc.Root.Elements()
						where itemGroupElement.Name.LocalName == "PropertyGroup"
						from item in itemGroupElement.Elements()
						select item;

			var outputPaths = from item in items
							  where item.Name.LocalName == "OutputPath"
							  select item;

			foreach (var outputPath in outputPaths)
			{
				var outPath = outputPath.Value;
				if (!outPath.Contains("\\")) continue;
				var folders = outPath.Split('\\').Where(x=>!string.IsNullOrEmpty(x));
				var newPath = Path.Combine(relativePath, folders.LastOrDefault()) + "\\";
				if (!outPath.Equals(newPath))
				{
					wasChanged = true;
					outputPath.Value = newPath;
				}
			}
		    return wasChanged;
		}

	    private static bool RewriteReferences(HashSet<string> assemblies, XDocument doc)
	    {
		    bool wasChanged = false;
		    var items = from itemGroupElement in doc.Root.Elements()
			    where itemGroupElement.Name.LocalName == "ItemGroup"
			    from item in itemGroupElement.Elements()
			    select item;

		    var refElements = from item in items
			    where item.Name.LocalName == "Reference"
			    select item;

		    foreach (var refElement in refElements)
		    {
			    string assemblyName = refElement.Attributes().Single(attr => attr.Name.LocalName == "Include").Value;
			    string filename = assemblyName.Split(',')[0];
			    var isPrivateElement = refElement.Elements().FirstOrDefault(e => e.Name.LocalName == "Private");
			    bool isPrivate = isPrivateElement == null ||
			                     !String.Equals(isPrivateElement.Value, "false", StringComparison.InvariantCultureIgnoreCase);
			    if (!isPrivate) continue;
			    bool firstTime = assemblies.Add(filename);
			    if (firstTime) continue;

			    wasChanged = true;
			    if (isPrivateElement == null)
			    {
				    isPrivateElement = new XElement(doc.Root.GetDefaultNamespace() + "Private", "False");
				    refElement.Add(isPrivateElement);
			    }
			    else
			    {
				    isPrivateElement.Value = "False";
			    }
		    }
		    return wasChanged;
	    }

	    private static void Restore(string fullCsprojPath)
        {
            if (File.Exists(fullCsprojPath + ".backup"))
            {
                File.Delete(fullCsprojPath);
                File.Move(fullCsprojPath + ".backup", fullCsprojPath);
            }
        }

        private static void StripReadonlyIfSet(string filename)
        {
            var outputFileAttributes = File.GetAttributes(filename);
            if ((outputFileAttributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(filename, outputFileAttributes ^ FileAttributes.ReadOnly);
        }

		public static string MakeRelativePath(string fromPath, string toPath)
		{
			if (string.IsNullOrEmpty(fromPath)) throw new ArgumentNullException("fromPath");
			if (string.IsNullOrEmpty(toPath)) throw new ArgumentNullException("toPath");

			var fromUri = new Uri(fromPath);
			var toUri = new Uri(toPath);

			if (fromUri.Scheme != toUri.Scheme) { return toPath; } // path can't be made relative.

			var relativeUri = fromUri.MakeRelativeUri(toUri);
			var relativePath = Uri.UnescapeDataString(relativeUri.ToString());

			if (toUri.Scheme.ToUpperInvariant() == "FILE")
			{
				relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			}

			return relativePath;
		}
	}
}