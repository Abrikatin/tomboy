
using System;
using System.Collections;
using System.IO;
using System.Xml;
using Mono.Posix;

namespace Tomboy
{
	public delegate void NoteRenameHandler (Note sender, string old_title);

	public class Note 
	{
		string filepath;

		DateTime change_date;
		bool save_needed;
		bool is_new;

		// Accessed by NoteArchiver...
		internal string title;
		internal string text;
		internal int width, height;
		internal int x, y;
		internal int cursor_pos;

		NoteManager manager;
		NoteWindow window;
		NoteBuffer buffer;

		InterruptableTimeout save_timeout;

		Note ()
		{
			save_timeout = new InterruptableTimeout ();
			save_timeout.Timeout += SaveTimeout;
		}

		// Create a new note stored in a file...
		public Note (string title, string filepath, NoteManager manager) 
			: this ()
		{
			this.title = title;
			this.filepath = filepath;
			this.manager = manager;
			this.is_new = true;
			this.change_date = DateTime.Now;
		}

		// Internal constructor, used when loading from an exising filepath.
		Note (string filepath, NoteManager manager)
			: this ()
		{
			this.manager = manager;
			this.filepath = filepath;
			this.is_new = false;
			this.change_date = File.GetLastWriteTime (filepath);
		}

		public void Delete ()
		{
			save_timeout.Cancel ();

			if (window != null) {
				window.Hide ();
				window.Destroy ();
			}
		}

		// Load from an existing Note...
		public static Note Load (string read_file, NoteManager manager)
		{
			Note note = new Note (read_file, manager);
			NoteArchiver.Read (read_file, note);
			return note;
		}

		public void Save () 
		{
			// Do nothing if we don't need to save.  Avoids unneccessary saves
			// e.g on forced quit when we call save for every note.
			if (!save_needed)
				return;

			Console.WriteLine ("Saving '{0}'...", title);

			NoteArchiver.Write (filepath, this);
		}

		//
		// Buffer change signals.  These queue saves and invalidate the serialized text
		// depending on the change...
		//

		void BufferChanged (object sender, EventArgs args)
		{
			QueueSave (true);
		}

		void BufferTagApplied (object sender, Gtk.TagAppliedArgs args)
		{
			if (NoteTagTable.TagIsIgnored (args.Tag))
				return;

			QueueSave (true);
		}

		void BufferTagRemoved (object sender, Gtk.TagRemovedArgs args)
		{
			if (NoteTagTable.TagIsIgnored (args.Tag))
				return;

			QueueSave (true);
		}

		void BufferInsertMarkSet (object sender, Gtk.MarkSetArgs args)
		{
			if (args.Mark != buffer.InsertMark)
				return;

			cursor_pos = args.Location.Offset;

			QueueSave (false);
		}

		//
		// Window events.  Queue a save when the window location/size has changed, and set
		// our window to null on delete, and fire the Opened event on window realize...
		//

		[GLib.ConnectBefore]
		void WindowConfigureEvent (object sender, Gtk.ConfigureEventArgs args)
		{
			int cur_x, cur_y, cur_width, cur_height;

			// Ignore events when maximized.  We don't want notes
			// popping up maximized the next run.
			if ((window.GdkWindow.State & Gdk.WindowState.Maximized) > 0)
				return;

			window.GetPosition (out cur_x, out cur_y);
			window.GetSize (out cur_width, out cur_height);

			if (x == cur_x && 
			    y == cur_y &&
			    width == cur_width && 
			    height == cur_height)
				return;

			x = cur_x;
			y = cur_y;
			width = cur_width;
			height = cur_height;
			
			QueueSave (false);
		}

		[GLib.ConnectBefore]
		void WindowDestroyed (object sender, EventArgs args) 
		{
			window = null;
		}

		// Set a 4 second timeout to execute the save.  Possibly invalid the text, which
		// causes a re-serialize when the timeout is called...
		public void QueueSave (bool invalidate_text)
		{
			// Replace the existing save timeout.  Wait 4 seconds
			// before saving...
			save_timeout.Reset (4000);
			save_needed = true;

			// Force a re-get of text on save
			if (invalidate_text) {
				change_date = DateTime.Now;
				text = null;
			}
		}

		// Save timeout to avoid constanly resaving.  Called every 4 seconds.
		void SaveTimeout (object sender, EventArgs args)
		{
			try {
				Save ();
				save_needed = false;
			} catch (Exception e) {
				// FIXME: Present a nice dialog here that interprets the
				// error message correctly.
				Console.WriteLine ("Error while saving: {0}", e);
			}
		}

		public string Uri
		{
			get { 
				return "note://tomboy/" + 
					Path.GetFileNameWithoutExtension (filepath); 
			}
		}

		public string FilePath 
		{
			get { return filepath; }
			set { filepath = value; }
		}

		public string Title 
		{
			get { return title; }
			set {
				if (title != value) {
					if (window != null)
						window.Title = value;

					string old_title = title;
					title = value;

					if (Renamed != null)
						Renamed (this, old_title);
				}
			}
		}

		public string XmlContent 
		{
			get {
				if (text == null && buffer != null) {
					Console.WriteLine ("Re-serializing buffer...");
					text = NoteBufferArchiver.Serialize (buffer); 
				}
				return text;
			}
			set {
				if (buffer != null) {
					buffer.Clear ();
					buffer.Undoer.FreezeUndo ();
					NoteBufferArchiver.Deserialize (buffer, 
									buffer.StartIter, 
									value);
					buffer.Undoer.ThawUndo ();
				}

				text = value;
				QueueSave (false);
			}
		}

		public string TextContent
		{
			get {
				if (buffer != null)
					return buffer.GetSlice (buffer.StartIter, 
								buffer.EndIter, 
								false /* hidden_chars */);
				else 
					return XmlDecoder.Decode (XmlContent);
			}
		}

		public DateTime ChangeDate 
		{
			get { return change_date; }
		}

		public NoteManager Manager
		{
			get { return manager; }
			set { manager = value; }
		}

		public NoteBuffer Buffer
		{
			get {
				if (buffer == null) {
					Console.WriteLine ("Creating Buffer for '{0}'...", title);

#if FIXED_GTKSPELL
					// NOTE: Sharing the same TagTable means
					// that formatting is duplicated between
					// buffers.
					buffer = new NoteBuffer (NoteTagTable.Instance);
#else
					// NOTE: GtkSpell chokes on shared
					// TagTables because it blindly tries to
					// create a new "gtkspell-misspelling"
					// tag, which fails if one already
					// exists in the table.
					buffer = new NoteBuffer (new NoteTagTable ());
#endif

					// Don't create Undo actions during load
					buffer.Undoer.FreezeUndo ();

					// Load the stored xml text
					NoteBufferArchiver.Deserialize (buffer, 
									buffer.StartIter, 
									text);
					buffer.Modified = false;

					// Move cursor to last-saved position
					Gtk.TextIter cursor;
					if (cursor_pos != 0)
						cursor = buffer.GetIterAtOffset (cursor_pos);
					else
						cursor = buffer.GetIterAtLine (2); // avoid title line
					buffer.PlaceCursor (cursor);

					// New events should create Undo actions
					buffer.Undoer.ThawUndo ();

					// Listen for further changed signals
					buffer.Changed += BufferChanged;
					buffer.TagApplied += BufferTagApplied;
					buffer.TagRemoved += BufferTagRemoved;
					buffer.MarkSet += BufferInsertMarkSet;
				}
				return buffer;
			}
		}

		public NoteWindow Window 
		{
			get {
				if (window == null) {
					window = new NoteWindow (this);
					window.Destroyed += WindowDestroyed;
					window.ConfigureEvent += WindowConfigureEvent;
					
					if (width != 0 && height != 0)
						window.SetDefaultSize (width, height);

					// Center new notes on screen
					if (x == 0 && y == 0)
						window.SetPosition (Gtk.WindowPosition.Center);
					else
						window.Move (x, y);

					// This is here because emiting
					// OnRealized causes segfaults.
					if (Opened != null)
						Opened (this, new EventArgs ());
				}
				return window; 
			}
		}

		public bool IsSpecial 
		{
			get { return title == Catalog.GetString ("Start Here"); }
		}

		public bool IsNew 
		{
			get { return is_new; }
		}

		public bool IsLoaded 
		{
			get { return buffer != null; }
		}

		public bool IsOpened 
		{
			get { return window != null; }
		}

		public event EventHandler Opened;
		public event NoteRenameHandler Renamed;
	}

	public class NoteArchiver
	{
		public static void Read (string read_file, Note note) 
		{
			StreamReader reader = new StreamReader (read_file, System.Text.Encoding.UTF8);
			XmlTextReader xml = new XmlTextReader (reader);
			xml.Namespaces = false;

			while (xml.Read ()) {
				switch (xml.NodeType) {
				case XmlNodeType.Element:
					switch (xml.Name) {
					case "note":
						break;
					case "title":
						note.title = xml.ReadString ();
						break;
					case "text":
						// <text> is just a wrapper around <note-content>
						// NOTE: Use .text here to avoid triggering a save.
						note.text = xml.ReadInnerXml ();
						break;
					case "cursor-position":
						note.cursor_pos = int.Parse (xml.ReadString ());
						break;
					case "width":
						note.width = int.Parse (xml.ReadString ());
						break;
					case "height":
						note.height = int.Parse (xml.ReadString ());
						break;
					case "x":
						note.x = int.Parse (xml.ReadString ());
						break;
					case "y":
						note.y = int.Parse (xml.ReadString ());
						break;
					}
					break;
				}
			}

			xml.Close ();
		}

		public static void Write (string write_file, Note note) 
		{
			string tmp_file = write_file + ".tmp";

			XmlTextWriter xml = new XmlTextWriter (tmp_file, System.Text.Encoding.UTF8);
			xml.Formatting = Formatting.Indented;

			xml.WriteStartDocument ();
			xml.WriteStartElement (null, "note", "http://beatniksoftware.com/tomboy");
			xml.WriteAttributeString("xmlns", 
						 "link", 
						 null, 
						 "http://beatniksoftware.com/tomboy/link");
			xml.WriteAttributeString("xmlns", 
						 "size", 
						 null, 
						 "http://beatniksoftware.com/tomboy/size");

			xml.WriteStartElement (null, "title", null);
			xml.WriteString (note.title);
			xml.WriteEndElement ();

			xml.WriteStartElement (null, "text", null);
			xml.WriteAttributeString ("xml", "space", null, "preserve");
			// Insert <note-content> blob...
			// NOTE: Use .XmlContent here to force a reget of text
			//       from the buffer, if needed.
			xml.WriteRaw (note.XmlContent);
			xml.WriteEndElement ();

			xml.WriteStartElement (null, "cursor-position", null);
			xml.WriteString (note.cursor_pos.ToString ());
			xml.WriteEndElement ();

			xml.WriteStartElement (null, "width", null);
			xml.WriteString (note.width.ToString ());
			xml.WriteEndElement ();

			xml.WriteStartElement (null, "height", null);
			xml.WriteString (note.height.ToString ());
			xml.WriteEndElement ();

			xml.WriteStartElement (null, "x", null);
			xml.WriteString (note.x.ToString ());
			xml.WriteEndElement ();

			xml.WriteStartElement (null, "y", null);
			xml.WriteString (note.y.ToString ());
			xml.WriteEndElement ();

			xml.WriteEndElement (); // Note
			xml.WriteEndDocument ();
			xml.Close ();

			// Backup the to a ~ file, just in case...
			if (File.Exists (write_file)) {
				string backup_path = write_file + "~";

				if (File.Exists (backup_path))
					File.Delete (backup_path);

				File.Move (write_file, backup_path);
			}

			// Move the temp file to write_file
			File.Move (tmp_file, write_file);
		}
	}
}
