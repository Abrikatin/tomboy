
using System;
using System.Runtime.InteropServices;
using Mono.Unix;

using Gtk;

using Tomboy;

namespace Tomboy.PrintNotes
{
	public class PrintNotesNoteAddin : NoteAddin
	{
		Gtk.ImageMenuItem item;

		public override void Initialize ()
		{
		}

		public override void Shutdown ()
		{
			// Disconnect the event handlers so
			// there aren't any memory leaks.
			if (item != null)
				item.Activated -= PrintButtonClicked;
		}

		public override void OnNoteOpened ()
		{
			item = new Gtk.ImageMenuItem (Catalog.GetString ("Print"));
			item.Image = new Gtk.Image (Gtk.Stock.Print, Gtk.IconSize.Menu);
			item.Activated += PrintButtonClicked;
			item.Show ();
			AddPluginMenuItem (item);
		}

		[DllImport("libprintnotes")]
		static extern void gedit_print (IntPtr text_view_handle);

		//
		// Handle Print menu item Click
		//

		void PrintButtonClicked (object sender, EventArgs args)
		{
			gedit_print (Note.Window.Editor.Handle);
		}
	}
}
