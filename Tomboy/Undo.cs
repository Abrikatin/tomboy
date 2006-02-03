
using System;
using System.Collections;

namespace Tomboy 
{
	interface EditAction 
	{
		void Undo (Gtk.TextBuffer buffer);
		void Redo (Gtk.TextBuffer buffer);
		void Merge (EditAction action);
		bool CanMerge (EditAction action);
		void Destroy ();
	}

	class ChopBuffer : Gtk.TextBuffer
	{
		public ChopBuffer (Gtk.TextTagTable table)
			: base (table)
		{
		}

		public TextRange AddChop (Gtk.TextIter start_iter, Gtk.TextIter end_iter)
		{
			int start, end;
			Gtk.TextIter insertAt = EndIter;

			start = EndIter.Offset;
			InsertRange (ref insertAt, start_iter, end_iter);
			end = EndIter.Offset;

			return new TextRange (GetIterAtOffset (start), GetIterAtOffset (end));
		}
	}

	class InsertAction : EditAction
	{
		int index;
		bool is_paste;
		TextRange chop;

		public InsertAction (Gtk.TextIter start, 
				     string text, 
				     int length, 
				     ChopBuffer chop_buf)
		{
			this.index = start.Offset - length;
			// GTKBUG: No way to tell a 1-char paste.
			this.is_paste = length > 1;

			Gtk.TextIter index_iter = start.Buffer.GetIterAtOffset (index);
			this.chop = chop_buf.AddChop (index_iter, start);
		}

		public void Undo (Gtk.TextBuffer buffer)
		{
			Gtk.TextIter deleteStart = buffer.GetIterAtOffset (index);
			Gtk.TextIter deleteEnd = buffer.GetIterAtOffset (index + chop.Length);
			buffer.Delete (ref deleteStart, ref deleteEnd);
			buffer.MoveMark (buffer.InsertMark, deleteStart);
			buffer.MoveMark (buffer.SelectionBound, deleteStart);
		}

		public void Redo (Gtk.TextBuffer buffer)
		{
			Gtk.TextIter insertAt = buffer.GetIterAtOffset (index);
			buffer.InsertRange (ref insertAt, chop.Start, chop.End);

			buffer.MoveMark (buffer.SelectionBound, insertAt);
			buffer.MoveMark (buffer.InsertMark, 
					 buffer.GetIterAtOffset (index + chop.Length));
		}

		public void Merge (EditAction action)
		{
			InsertAction insert = (InsertAction) action;

			chop.End = insert.chop.End;

			insert.chop.Destroy ();
		}

		public bool CanMerge (EditAction action)
		{
			InsertAction insert = action as InsertAction;
			if (insert == null)
				return false;

			// Don't group text pastes
			if (is_paste || insert.is_paste)
				return false;

			// Must meet eachother
			if (insert.index != index + chop.Length)
				return false;

			// Don't group more than one line (inclusive)
			if (chop.Text[0] == '\n')
				return false;

			// Don't group more than one word (exclusive)
			if (insert.chop.Text[0] == ' ' || insert.chop.Text[0] == '\t')
				return false;

			return true;
		}

		public void Destroy ()
		{
			chop.Erase ();
			chop.Destroy ();
		}
	}

	class EraseAction : EditAction
	{
		int start;
		int end;
		bool is_forward;
		bool is_cut;
		TextRange chop;

		public EraseAction (Gtk.TextIter start_iter, 
				    Gtk.TextIter end_iter,
				    ChopBuffer chop_buf)
		{
			this.start = start_iter.Offset;
			this.end = end_iter.Offset;
			this.is_cut = end - start > 1;
			
			Gtk.TextIter insert = 
				start_iter.Buffer.GetIterAtMark (start_iter.Buffer.InsertMark);
			this.is_forward = insert.Offset <= start;

			this.chop = chop_buf.AddChop (start_iter, end_iter);
		}

		public void Undo (Gtk.TextBuffer buffer)
		{
			Gtk.TextIter insertAt = buffer.GetIterAtOffset (start);
			buffer.InsertRange (ref insertAt, chop.Start, chop.End);

			buffer.MoveMark (buffer.InsertMark, 
					 buffer.GetIterAtOffset (is_forward ? start : end));
			buffer.MoveMark (buffer.SelectionBound, 
					 buffer.GetIterAtOffset (is_forward ? end : start));
		}

		public void Redo (Gtk.TextBuffer buffer)
		{
			Gtk.TextIter deleteStart = buffer.GetIterAtOffset (start);
			Gtk.TextIter deleteEnd = buffer.GetIterAtOffset (end);
			
			buffer.Delete (ref deleteStart, ref deleteEnd);
			buffer.MoveMark (buffer.InsertMark, deleteStart);
			buffer.MoveMark (buffer.SelectionBound, deleteStart);
		}

		public void Merge (EditAction action)
		{
			EraseAction erase = (EraseAction) action;
			if (start == erase.start) {
				end += erase.end - erase.start;
				chop.End = erase.chop.End;

				// Delete the marks, leave the text
				erase.chop.Destroy ();
			} else {
				start = erase.start;

				Gtk.TextIter insertAt = chop.Start;
				chop.Buffer.InsertRange (ref insertAt, 
							 erase.chop.Start, 
							 erase.chop.End);

				// Delete the marks and text
				erase.Destroy ();
			}
		}

		public bool CanMerge (EditAction action)
		{
			EraseAction erase = action as EraseAction;
			if (erase == null)
				return false;

			// Don't group separate text cuts
			if (is_cut || erase.is_cut)
				return false;

			// Must meet eachother
			if (start != (is_forward ? erase.start : erase.end))
				return false;

			// Don't group deletes with backspaces
			if (is_forward != erase.is_forward)
				return false;

			// Don't group more than one line (inclusive)
			if (chop.Text[0] == '\n')
				return false;

			// Don't group more than one word (exclusive)
			if (erase.chop.Text[0] == ' ' || erase.chop.Text[0] == '\t')
				return false;

			return true;
		}

		public void Destroy ()
		{
			chop.Erase ();
			chop.Destroy ();
		}
	}

	class TagApplyAction : EditAction
	{
		Gtk.TextTag tag;
		int         start;
		int         end;
		
		public TagApplyAction (Gtk.TextTag tag, Gtk.TextIter start, Gtk.TextIter end)
		{
			this.tag = tag;
			this.start = start.Offset;
			this.end = end.Offset;
		}

		public void Undo (Gtk.TextBuffer buffer)
		{
			Gtk.TextIter start_iter, end_iter;
			start_iter = buffer.GetIterAtOffset (start);
			end_iter = buffer.GetIterAtOffset (end);
				
			buffer.MoveMark (buffer.SelectionBound, start_iter);
			buffer.RemoveTag (tag, start_iter, end_iter);
			buffer.MoveMark (buffer.InsertMark, end_iter);
		}

		public void Redo (Gtk.TextBuffer buffer)
		{
			Gtk.TextIter start_iter, end_iter;
			start_iter = buffer.GetIterAtOffset (start);
			end_iter = buffer.GetIterAtOffset (end);
				
			buffer.MoveMark (buffer.SelectionBound, start_iter);
			buffer.ApplyTag (tag, start_iter, end_iter);
			buffer.MoveMark (buffer.InsertMark, end_iter);
		}

		public void Merge (EditAction action)
		{
			throw new Exception ("TagApplyActions cannot be merged");
		}

		public bool CanMerge (EditAction action)
		{
			return false;
		}

		public void Destroy ()
		{
		}
	}

	class TagRemoveAction : EditAction
	{
		Gtk.TextTag tag;
		int         start;
		int         end;

		public TagRemoveAction (Gtk.TextTag tag, Gtk.TextIter start, Gtk.TextIter end)
		{
			this.tag = tag;
			this.start = start.Offset;
			this.end = end.Offset;
		}

		public void Undo (Gtk.TextBuffer buffer)
		{
			Gtk.TextIter start_iter, end_iter;
			start_iter = buffer.GetIterAtOffset (start);
			end_iter = buffer.GetIterAtOffset (end);

			buffer.MoveMark (buffer.SelectionBound, start_iter);
			buffer.ApplyTag (tag, start_iter, end_iter);
			buffer.MoveMark (buffer.InsertMark, end_iter);
		}

		public void Redo (Gtk.TextBuffer buffer)
		{
			Gtk.TextIter start_iter, end_iter;
			start_iter = buffer.GetIterAtOffset (start);
			end_iter = buffer.GetIterAtOffset (end);

			buffer.MoveMark (buffer.SelectionBound, start_iter);
			buffer.RemoveTag (tag, start_iter, end_iter);
			buffer.MoveMark (buffer.InsertMark, end_iter);
		}

		public void Merge (EditAction action)
		{
			throw new Exception ("TagRemoveActions cannot be merged");
		}

		public bool CanMerge (EditAction action)
		{
			return false;
		}

		public void Destroy ()
		{
		}
	}

	public class UndoManager
	{
		uint frozen_cnt;
		bool try_merge;
		NoteBuffer buffer;
		ChopBuffer chop_buffer;

		Stack undo_stack;
		Stack redo_stack;

		public UndoManager (NoteBuffer buffer)
		{
			frozen_cnt = 0;
			try_merge = false;
			undo_stack = new Stack ();
			redo_stack = new Stack ();

			this.buffer = buffer;
			chop_buffer = new ChopBuffer (buffer.TagTable);

			buffer.InsertTextWithTags += OnInsertText;
			buffer.DeleteRange += OnDeleteRange; // Before handler
			buffer.TagApplied += OnTagApplied;
			buffer.TagRemoved += OnTagRemoved;
		}

		public bool CanUndo
		{
			get { return undo_stack.Count > 0; }
		}

		public bool CanRedo 
		{
			get { return redo_stack.Count > 0; }
		}

		public event EventHandler UndoChanged;

		public void Undo ()
		{
			UndoRedo (undo_stack, redo_stack, true /*undo*/);
		}

		public void Redo ()
		{
			UndoRedo (redo_stack, undo_stack, false /*redo*/);
		}

		public void FreezeUndo ()
		{
			++frozen_cnt;
		}

		public void ThawUndo ()
		{
			--frozen_cnt;
		}

		void UndoRedo (Stack pop_from, Stack push_to, bool is_undo)
		{
			if (pop_from.Count > 0) {
				EditAction action = (EditAction) pop_from.Pop ();
				
				FreezeUndo ();
				if (is_undo)
					action.Undo (buffer);
				else 
					action.Redo (buffer);
				ThawUndo ();

				push_to.Push (action);

				// Lock merges until a new undoable event comes in...
				try_merge = false;

				if (pop_from.Count == 0 || push_to.Count == 1)
					if (UndoChanged != null)
						UndoChanged (this, new EventArgs ());
			}
		}

		void ClearActionStack (Stack stack)
		{
			foreach (EditAction action in stack) {
				action.Destroy ();
			}
			stack.Clear ();
		}

		public void ClearUndoHistory ()
		{
			ClearActionStack (undo_stack);
			ClearActionStack (redo_stack);

			if (UndoChanged != null) 
				UndoChanged (this, new EventArgs ());
		}

		void AddUndoAction (EditAction action)
		{
			if (try_merge && undo_stack.Count > 0) {
				EditAction top = (EditAction) undo_stack.Peek ();

				if (top.CanMerge (action)) {
					// Merging object should handle freeing
					// action's resources, if needed.
					top.Merge (action);
					return;
				}
			}

			undo_stack.Push (action);

			// Clear the redo stack
			ClearActionStack (redo_stack);

			// Try to merge new incoming actions...
			try_merge = true;

			// Have undoable actions now
			if (undo_stack.Count == 1) {
				if (UndoChanged != null)
					UndoChanged (this, new EventArgs ());
			}
		}

		// Action-creating event handlers...

		void OnInsertText (object sender, Gtk.InsertTextArgs args)
		{
			if (frozen_cnt == 0) {
				AddUndoAction (new InsertAction (args.Pos, 
								 args.Text, 
								 args.Length,
								 chop_buffer));
			}
		}

		[GLib.ConnectBefore]
		void OnDeleteRange (object sender, Gtk.DeleteRangeArgs args)
		{
			if (frozen_cnt == 0) {
				AddUndoAction (new EraseAction (args.Start, 
								args.End,
								chop_buffer));
			}
		}

		void OnTagApplied (object sender, Gtk.TagAppliedArgs args)
		{
			if (frozen_cnt == 0) {
				if (NoteTagTable.TagIsUndoable (args.Tag)) {
					AddUndoAction (new TagApplyAction (args.Tag,
									   args.StartChar,
									   args.EndChar));
				}
			}
		}

		void OnTagRemoved (object sender, Gtk.TagRemovedArgs args)
		{
			if (frozen_cnt == 0) {
				if (NoteTagTable.TagIsUndoable (args.Tag)) {
					// FIXME: Gtk# bug. StartChar and EndChar are not
					//        mapped, so grab them from the Args iter.
					Gtk.TextIter start, end;
					start = (Gtk.TextIter) args.Args[1];
					end = (Gtk.TextIter) args.Args[2];

					AddUndoAction (new TagRemoveAction (args.Tag, start, end));
				}
			}
		}
	}
}
