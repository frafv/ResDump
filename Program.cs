using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Collections;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System.Text.RegularExpressions;
using System.Xml;

namespace ResDump
{
	internal class BinaryReaderEx: BinaryReader
	{
		public BinaryReaderEx(Stream stream) : base(stream) { }

		public new int Read7BitEncodedInt()
		{
			return base.Read7BitEncodedInt();
		}
	}

	public class ServiceContainer: IServiceProvider
	{
		private Dictionary<Type, object> m_oServices = new Dictionary<Type, object>();

		public void AddService<T>(T service)
		{
			this.m_oServices.Add(typeof(T), service);
		}

		public object GetService(Type serviceType)
		{
			object obj2;
			this.m_oServices.TryGetValue(serviceType, out obj2);
			return obj2;
		}
	}

	internal static class WriterExtensions
	{
		internal static void WriteDocumentElement(this XmlWriter writer, string name, Action body)
		{
			writer.WriteStartDocument();
			writer.WriteStartElement(name);
			body();
			writer.WriteEndElement();
			writer.WriteEndDocument();
		}

		internal static void WriteElement(this XmlWriter writer, string name, Action body)
		{
			writer.WriteStartElement(name);
			body();
			writer.WriteEndElement();
		}

		internal static void WriteElement(this XmlWriter writer, string name, int value)
		{
			writer.WriteStartElement(name);
			writer.WriteString(XmlConvert.ToString(value));
			writer.WriteEndElement();
		}

		internal static void WriteElement(this XmlWriter writer, string name, float value)
		{
			writer.WriteStartElement(name);
			writer.WriteString(XmlConvert.ToString(value));
			writer.WriteEndElement();
		}

		internal static void WriteElement(this XmlWriter writer, string name, char? value)
		{
			if (!value.HasValue) return;
			writer.WriteStartElement(name);
			if (value.Value > ' ' && value.Value <= '~')
				writer.WriteString(value.Value.ToString());
			else
				writer.WriteString(String.Format("&#x{0:X};", (int)value.Value));
			writer.WriteEndElement();
		}

		internal static void WriteElement(this XmlWriter writer, string name, char value)
		{
			writer.WriteStartElement(name);
			writer.WriteString(String.Format("&#x{0:X};", (int)value));
			writer.WriteEndElement();
		}

		internal static void WriteElement(this XmlWriter writer, string name, string value)
		{
			if (value == null) return;
			writer.WriteStartElement(name);
			writer.WriteString(value);
			writer.WriteEndElement();
		}
	}

	class Program
	{
		static void Main(string[] args)
		{
			if (args.Length < 1)
			{
				throw new ArgumentNullException("args[0]");
			}

			int? h = null, w = null;
			if (args.Length > 1 && Regex.IsMatch(args[1], @"\d+x\d+"))
			{
				int sep = args[1].IndexOf('x');
				w = int.Parse(args[1].Substring(0, sep));
				h = int.Parse(args[1].Substring(sep + 1));
			}
			ReadFile(args[0], w, h);
		}

		static void ReadFile(string name, int? width, int? height)
		{
			if (name.Contains("*"))
			{
				string path = Path.GetDirectoryName(name.Replace('*', '_'));
				path = path == String.Empty ? Directory.GetCurrentDirectory() : Path.GetFullPath(path);
				int suf = Path.GetFileName(name.Replace('*', '_')).Length;
				foreach (string file in Directory.EnumerateFiles(path, name.Substring(name.Length - suf)))
				{
					Console.WriteLine("Process: " + file);
					ReadFile(file, width, height);
				}
				return;
			}

			string ext = Path.GetExtension(name).ToLower();
			switch (ext)
			{
			case ".exe":
			case ".dll":
				LoadResources(name);
				break;
			case ".xnb":
				LoadXNB(name, width, height);
				break;
			}
		}

		static byte[] ReadAll(Stream src)
		{
			var buf = new byte[src.Length];
			int k = src.Read(buf, 0, (int)src.Length);
			int m = k;
			while (m < src.Length)
			{
				k = src.Read(buf, m, (int)src.Length - m);
				m += k;
			}
			return buf;
		}

		static void LoadResources(string name)
		{
			var assm = Assembly.LoadFile(name);
			var ress = assm.GetManifestResourceNames();
			foreach (string nres in ress)
			{
				var res = assm.GetManifestResourceStream(nres);
				using (var rm = new ResourceReader(res))
				{
					foreach (DictionaryEntry n in rm)
					{
						var mem = n.Value as Stream;
						if (mem == null) continue;
						try
						{
							string fn = Path.Combine(nres, n.Key.ToString());
							string path = Path.GetDirectoryName(fn);
							if (!Directory.Exists(path))
								Directory.CreateDirectory(path);
							using (var fs = new FileStream(fn, FileMode.CreateNew))
							{
								var buf = ReadAll(mem);
								fs.Write(buf, 0, buf.Length);
							}
						}
						catch (Exception ex)
						{
							Console.WriteLine(ex.ToString());
						}
					}
				}
			}
		}

		static void LoadXNB(string name, int? width, int? height)
		{
			var types = new List<string>();
			bool fixPlatform = false;
			using(var fs = new FileStream(name, FileMode.Open))
			using (var reader = new BinaryReaderEx(fs))
			{
				var sig = reader.ReadChars(3);
				if (sig[0] != 'X' || sig[1] != 'N' || sig[2] != 'B')
					throw new NotSupportedException();
				var platform = reader.ReadChar();
				switch (platform)
				{
				case 'w':
					Console.WriteLine("Target platform: Microsoft Windows");
					break;
				case 'm':
					Console.WriteLine("Target platform: Windows Phone 7");
					fixPlatform = true;
					break;
				case 'x':
					Console.WriteLine("Target platform: Xbox 360");
					fixPlatform = true;
					break;
				default:
					throw new NotSupportedException();
				}
				var ver = reader.ReadByte();
				Console.WriteLine("XNB format version: " + (ver == 5 ? "XNA Game Studio 4.0" : "Unknown version " + ver.ToString()));
				var flags = reader.ReadByte();
				Console.WriteLine("Flags: {0:X}", flags);
				int size = reader.ReadInt32();
				int count = reader.Read7BitEncodedInt();
				for (int k = 0; k < count; k++)
				{
					string type = reader.ReadString();
					Console.WriteLine("Found type: {0}", type);
					uint tv = reader.ReadUInt32();
					if (tv != 0)
						Console.WriteLine("Read version: {0:X}", tv);
					types.Add(type);
				}
			}

			string path = Path.GetDirectoryName(name);
			string dest = Path.GetFileNameWithoutExtension(name);
			if (fixPlatform)
			{
				var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xnb");
				using (var fs1 = new FileStream(name, FileMode.Open))
				using (var fs2 = new FileStream(tmp, FileMode.CreateNew))
				{
					var buf = ReadAll(fs1);
					buf[3] = (byte)'w';
					fs2.Write(buf, 0, buf.Length);
				}
				name = tmp;
			}

			var game = new Game();
			var dev = new GraphicsDeviceManager(game);
			dev.ApplyChanges();
			var svc = new ServiceContainer();
			svc.AddService<IGraphicsDeviceService>(dev);
			ContentManager man = new ContentManager(svc, Path.GetDirectoryName(name));
			string asset = Path.GetFileNameWithoutExtension(name);

			for (int k = 0; k < types.Count; k++)
			{
				if (types[k].StartsWith("Microsoft.Xna.Framework.Content.SpriteFontReader"))
				{
					var font = man.Load<SpriteFont>(asset);
					var fi = font.GetType().GetField("textureValue", BindingFlags.NonPublic | BindingFlags.Instance);
					var tex = (Texture2D)fi.GetValue(font);
					using (var writer = new FileStream(Path.Combine(path, dest + ".png"), FileMode.CreateNew))
					{
						tex.SaveAsPng(writer, tex.Width, tex.Height);
					}
					GenSpriteFont(font, Path.Combine(path, dest + ".spritefont"));

					Console.WriteLine("Font line spacing: {0}", font.LineSpacing);
					Console.WriteLine("Font spacing: {0}", font.Spacing);
					if (font.DefaultCharacter.HasValue)
						Console.WriteLine("Default character code: &#{0} &#x{0:X}", (int)font.DefaultCharacter.Value);
					var v = font.MeasureString("Hello World!");
					Console.WriteLine("Measure \"Hello World!\": {0}x{1}", v.X, v.Y);
					int Eindex = font.Characters.IndexOf('8');
					int Aindex = font.Characters.IndexOf('*');
					int Windex = font.Characters.IndexOf('W');
					int Xindex = font.Characters.IndexOf('x');
					fi = font.GetType().GetField("glyphData", BindingFlags.NonPublic | BindingFlags.Instance);
					var glyphData = (List<Rectangle>)fi.GetValue(font);
					fi = font.GetType().GetField("croppingData", BindingFlags.NonPublic | BindingFlags.Instance);
					var croppingData = (List<Rectangle>)fi.GetValue(font);
					if (Eindex >= 0)
						Console.WriteLine("'8' glyph: {0}x{1}; cropping: {2}x{3}", glyphData[Eindex].Width, glyphData[Eindex].Height, croppingData[Eindex].Width, croppingData[Eindex].Height);
					if (Aindex >= 0)
						Console.WriteLine("'*' glyph: {0}x{1}; cropping: {2}x{3}", glyphData[Aindex].Width, glyphData[Aindex].Height, croppingData[Aindex].Width, croppingData[Aindex].Height);
					if (Windex >= 0)
						Console.WriteLine("'W' glyph: {0}x{1}; cropping: {2}x{3}", glyphData[Eindex].Width, glyphData[Windex].Height, croppingData[Windex].Width, croppingData[Windex].Height);
					if (Xindex >= 0)
						Console.WriteLine("'x' glyph: {0}x{1}; cropping: {2}x{3}", glyphData[Xindex].Width, glyphData[Xindex].Height, croppingData[Xindex].Width, croppingData[Xindex].Height);
					break;
				}
				else if (types[k].StartsWith("Microsoft.Xna.Framework.Content.Texture2DReader"))
				{
					var tex = man.Load<Texture2D>(asset);
					using (var writer = new FileStream(Path.Combine(path, dest + ".png"), FileMode.CreateNew))
					{
						tex.SaveAsPng(writer, width ?? tex.Width, height ?? tex.Height);
					}
					break;
				}
			}

			if (fixPlatform)
				File.Delete(name);
		}

		static void GenSpriteFont(SpriteFont font, string name)
		{
			using(var writer = XmlWriter.Create(name, new XmlWriterSettings() {
				Encoding = Encoding.UTF8,
				Indent = true,
				IndentChars = "\t",
				OmitXmlDeclaration = false
			}))
			{
				writer.WriteDocumentElement("XnaContent", () =>
				{
					writer.WriteAttributeString("xmlns", "Graphics", null, "Microsoft.Xna.Framework.Content.Pipeline.Graphics");
					writer.WriteElement("Asset", () =>
					{
						writer.WriteAttributeString("Type", "Graphics:FontDescription");

						writer.WriteComment("\n\t\tModify this string to change the font that will be imported.\n\t\t" +
							"Current string is taken from the XNB file name.\n\t\t");
						writer.WriteElement("FontName", Path.GetFileNameWithoutExtension(name));

						writer.WriteComment("\n\t\tSize is a float value, measured in points. Modify this value to change\n\t\t" +
							"the size of the font.\n\t\t" +
							"Current size is calculated by XNB fonts text measuring.\n\t\t");
						var size = font.MeasureString("SIZE");
						writer.WriteElement("Size", (int)Math.Round(size.Y / 2.0f));

						writer.WriteElement("Spacing", font.Spacing);

						writer.WriteComment("\n\t\tUseKerning controls the layout of the font. If this value is true, kerning information\n\t\t" +
							"will be used when placing characters.\n\t\t");
						writer.WriteElement("UseKerning", "true");

						writer.WriteComment("\n\t\tStyle controls the style of the font. Valid entries are \"Regular\", \"Bold\", \"Italic\",\n\t\t" +
							"and \"Bold, Italic\", and are case sensitive.\n\t\t");
						writer.WriteElement("Style", "Regular");

						if (font.DefaultCharacter.HasValue)
							writer.WriteElement("DefaultCharacter", font.DefaultCharacter);

						writer.WriteElement("CharacterRegions", () =>
						{
							var chars = font.Characters;
							for (int k = 0; k <= chars.Count; k++)
							{
								if (k == 0 || k == chars.Count || (int)chars[k - 1] + 1 != (int)chars[k])
								{
									if (k > 0)
									{
										writer.WriteElement("End", chars[k - 1]);
										writer.WriteEndElement(); //CharacterRegion
									}
									if (k < chars.Count)
									{
										writer.WriteStartElement("CharacterRegion");
										writer.WriteElement("Start", chars[k]);
									}
								}
							}
						});
					});
				});
			}
		}
	}
}
