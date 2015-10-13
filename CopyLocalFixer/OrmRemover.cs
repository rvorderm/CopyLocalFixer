using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CopyLocalFixer
{
	public class OrmRemover
	{
		private OrmRemover()
		{
			
		}

		public static OrmRemover Instance => new Lazy<OrmRemover>(()=> new OrmRemover()).Value;

		private const string Pattern = "Orm:(User|Auto):[A-Fa-f0-9]{8}(?:-[A-Fa-f0-9]{4}){3}-[A-Fa-f0-9]{12}";
		private readonly Regex _regex = new Regex(Pattern, RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

		public bool StripOrmTags(XDocument doc)
		{
			bool wasChanged = false;
			var groups = from itemGroupElement in doc.Root.Elements()
						 where itemGroupElement.Name.LocalName == "ItemGroup"
						 from item in itemGroupElement.Elements()
						 select item;

			var compileElements = from item in groups
								  where item.Name.LocalName == "Compile"
								  select item;

			var ormTags = from item in compileElements.SelectMany(x=>x.Elements())
						  where item.Name.LocalName == "CustomToolNamespace"
						  select item;

			foreach (var tag in ormTags.ToList())
			{
				Match match = _regex.Match(tag.Value);
				if (match.Success)
				{
					tag.Remove();
					wasChanged = true;
				}
			}
			return wasChanged;
		}
	}
}
