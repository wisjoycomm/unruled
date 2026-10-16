// Copyright (C) KitWright. Licensed under MIT.

using System;
using UnityEngine.UIElements;

namespace KitWright.Editor.MCP.Server
{
    internal interface IMCPWindowPanel : IDisposable
    {
        void Build(VisualElement container);
    }
}
