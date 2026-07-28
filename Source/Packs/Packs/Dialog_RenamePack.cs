using UnityEngine;
using Verse;

namespace Packs;

public sealed class Dialog_RenamePack : Window
{
	private readonly PackRecord record;

	private string name;

	public override Vector2 InitialSize => new Vector2(500f, 190f);

	public Dialog_RenamePack(PackRecord record)
	{
		this.record = record;
		name = record?.Label ?? string.Empty;
		doCloseX = true;
		absorbInputAroundWindow = true;
		forcePause = true;
	}

	public override void DoWindowContents(Rect inRect)
	{
		Text.Font = GameFont.Medium;
		Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "Rename Predator Group");
		Text.Font = GameFont.Small;
		GUI.SetNextControlName("PackNameField");
		name = Widgets.TextField(new Rect(0f, 48f, inRect.width, 36f), name ?? string.Empty);
		GUI.FocusControl("PackNameField");
		bool flag = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
		if (Widgets.ButtonText(new Rect(inRect.width - 250f, inRect.height - 42f, 116f, 36f), "Cancel"))
		{
			Close();
		}
		if (Widgets.ButtonText(new Rect(inRect.width - 124f, inRect.height - 42f, 124f, 36f), "Save") || flag)
		{
			if (flag)
			{
				Event.current.Use();
			}
			string text = (name ?? string.Empty).Trim();
			record.name = (string.IsNullOrEmpty(text) ? (record.species.LabelCap.ToString() + " " + record.id) : text);
			Close();
		}
	}
}
