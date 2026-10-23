using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unruled.View.UI
{
    /// <summary>
    /// The one modal overlay of the main screen: a title, an icon, a scrolling body and up to three buttons. Confirm
    /// dialogs (risky orders), the alerts log, the pause menu and the end screens all open through it, one at a time.
    /// A full-screen variant shows a backdrop image instead of dimming the board. Layout only; the callbacks it runs
    /// belong to the panel that opened it.
    /// </summary>
    public sealed class ModalPopup
    {
        private const string DangerClass = "modal--danger";
        private const string VictoryClass = "modal--victory";
        private const string ScreenClass = "overlay--screen";

        public enum Tone : byte
        {
            Normal = 0,
            Danger = 1,
            Victory = 2
        }

        private readonly VisualElement _overlay, _backdrop, _modal, _icon, _actions;
        private readonly Label _title, _body;
        private readonly Button[] _buttons = new Button[3];
        private readonly Action[] _onClick = new Action[3];
        private readonly bool[] _closes = new bool[3];

        public ModalPopup(VisualElement root)
        {
            _overlay = TopTelemetryHud.Require<VisualElement>(root, "overlay");
            _backdrop = TopTelemetryHud.Require<VisualElement>(root, "overlay-backdrop");
            _modal = TopTelemetryHud.Require<VisualElement>(root, "modal");
            _icon = TopTelemetryHud.Require<VisualElement>(root, "modal-icon");
            _actions = TopTelemetryHud.Require<VisualElement>(root, "modal-actions");
            _title = TopTelemetryHud.Require<Label>(root, "modal-title");
            _body = TopTelemetryHud.Require<Label>(root, "modal-body");
            for (int i = 0; i < _buttons.Length; i++)
            {
                int index = i;
                _buttons[i] = new Button { name = "modal-button-" + UiFormat.Number(i) };
                _buttons[i].AddToClassList("u-btn");
                _buttons[i].clicked += () => Click(index);
                _actions.Add(_buttons[i]);
            }
        }

        public bool IsOpen => _overlay.style.display == DisplayStyle.Flex;

        /// <summary>Asks before a risky or costly order; <paramref name="confirm"/> runs only on Confirm.</summary>
        public void Confirm(string title, string body, Texture2D icon, string confirmText, Action confirm, Tone tone = Tone.Danger)
        {
            Open(title, body, icon, tone, null);
            SetButton(0, "Cancel", "u-btn--secondary", null, true);
            SetButton(1, null, null, null, true);
            SetButton(2, confirmText, tone == Tone.Danger ? "u-btn--danger" : null, confirm, true);
        }

        /// <summary>An information dialog with one button that closes it.</summary>
        public void Inform(string title, string body, Texture2D icon, Tone tone = Tone.Normal)
        {
            Open(title, body, icon, tone, null);
            SetButton(0, null, null, null, true);
            SetButton(1, null, null, null, true);
            SetButton(2, "Close", null, null, true);
        }

        /// <summary>
        /// A dialog with up to three labelled actions (null label hides a button). A null <paramref name="backdrop"/>
        /// dims the board; a texture fills the screen behind the dialog.
        /// </summary>
        public void Menu(string title, string body, Texture2D icon, Tone tone, Texture2D backdrop,
            string first, Action onFirst, string second, Action onSecond, string third, Action onThird)
        {
            Open(title, body, icon, tone, backdrop);
            SetButton(0, first, "u-btn--secondary", onFirst, true);
            SetButton(1, second, "u-btn--secondary", onSecond, true);
            SetButton(2, third, null, onThird, true);
        }

        /// <summary>Rewrites the body while open (live readouts, e.g. the alerts log).</summary>
        public void SetBody(string body) => UiFormat.Set(_body, body);

        public void Close()
        {
            _overlay.style.display = DisplayStyle.None;
            for (int i = 0; i < _onClick.Length; i++)
                _onClick[i] = null;
        }

        private void Open(string title, string body, Texture2D icon, Tone tone, Texture2D backdrop)
        {
            _title.text = title;
            _body.text = body;
            _icon.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);
            _icon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;
            _modal.EnableInClassList(DangerClass, tone == Tone.Danger);
            _modal.EnableInClassList(VictoryClass, tone == Tone.Victory);
            _overlay.EnableInClassList(ScreenClass, backdrop != null);
            _backdrop.style.backgroundImage = backdrop != null ? new StyleBackground(backdrop) : new StyleBackground(StyleKeyword.None);
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.BringToFront();
        }

        private void SetButton(int index, string text, string variant, Action onClick, bool closes)
        {
            var button = _buttons[index];
            button.style.display = text != null ? DisplayStyle.Flex : DisplayStyle.None;
            button.text = text ?? string.Empty;
            button.EnableInClassList("u-btn--secondary", variant == "u-btn--secondary");
            button.EnableInClassList("u-btn--danger", variant == "u-btn--danger");
            _onClick[index] = onClick;
            _closes[index] = closes;
        }

        private void Click(int index)
        {
            var action = _onClick[index];
            if (_closes[index])
                Close();
            action?.Invoke();
        }
    }
}
