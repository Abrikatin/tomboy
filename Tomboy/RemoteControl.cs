
using System;
using System.Collections;

using NDesk.DBus;
using org.freedesktop.DBus;

namespace Tomboy
{
	[Interface ("org.gnome.Tomboy.RemoteControl")]
	public class RemoteControl : MarshalByRefObject
	{
		private NoteManager note_manager;

		public RemoteControl (NoteManager mgr)
		{
			note_manager = mgr;
		}

		//Convert System.DateTime to unix timestamp
		private static long UnixDateTime(DateTime d)
		{
			long epoch_ticks = new DateTime (1970,1,1).Ticks;
			//Ticks is in 100s of nanoseconds, unix time is in seconds
			return (d.ToUniversalTime ().Ticks - epoch_ticks) / 10000000;
		}
	
		public bool DisplayNote (string uri)
		{
			Note note;

			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;

			note.Window.Present ();
			return true;
		}

		public bool DisplayNoteWithSearch (string uri, string search)
		{
			Note note;

			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;

			note.Window.Present ();

			// Pop open the find-bar
			NoteFindBar find = note.Window.Find;
			find.ShowAll ();
			find.Visible = true;
			find.SearchText = search;

			return true;
		}

		public string FindNote (string linked_title)
		{
			Note note = note_manager.Find (linked_title);
			return (note == null) ? "" : note.Uri;
		}

		public string CreateNote ()
		{
			try {
				Note note = note_manager.Create ();
				return note.Uri;
			} catch {
				return  "";
			}
		}

		public string CreateNamedNote (string linked_title)
		{
			Note note;
			
			note = note_manager.Find (linked_title);
			if (note != null)
				return "";

			try {
				note = note_manager.Create (linked_title);
				return note.Uri;
			} catch {
				return "";
			}
		}

		public bool DeleteNote (string uri)
		{
			Note note;

			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;

			note_manager.Delete (note);
			return true;
		}
		
		public void DisplaySearch ()
		{
			NoteRecentChanges.GetInstance (note_manager).Present ();
		}
		
		public void DisplaySearchWithText (string search_text)
		{
			NoteRecentChanges recent_changes =
				NoteRecentChanges.GetInstance (note_manager);
			if (recent_changes == null)
				return;
			
			recent_changes.SearchText = search_text;
			recent_changes.Present ();
		}

		public bool NoteExists (string uri)
		{
			Note note = note_manager.FindByUri (uri);
			return note != null;
		}

		public string[] ListAllNotes ()
		{
			ArrayList uris = new ArrayList ();
			foreach (Note note in note_manager.Notes) {
				uris.Add (note.Uri);
			}
			return (string []) uris.ToArray (typeof (string)) ;
		}

		public string GetNoteContents (string uri)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return "";
			return note.TextContent;
		}

		public string GetNoteTitle (string uri)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return "";
			return note.Title;
		}

		public long GetNoteCreateDate (string uri)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return -1;
			return UnixDateTime (note.CreateDate);
		}

		public long GetNoteChangeDate (string uri)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return -1;
			return UnixDateTime (note.ChangeDate);
		}

		public string GetNoteContentsXml (string uri)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return "";
			return note.XmlContent;
		}

		public bool SetNoteContents (string uri, string text_contents)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;
			note.TextContent = text_contents;
			return true;
		}

		public bool SetNoteContentsXml (string uri, string xml_contents)
		{
			Note note;
			note = note_manager.FindByUri (uri);
			if (note == null)
				return false;
			note.XmlContent = xml_contents;
			return true;
		}
	}
}
