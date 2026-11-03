using System.Collections.Generic;
using TerakkiFramework.Mvvm;
using TerakkiFramework.Plc.Abstractions;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>Base class for every operator screen: carries the shared runtime and exposes the tag
    /// dictionary for XAML indexer binding (<c>Tags[NAME]</c> / <c>Tags[NAME].TagValue</c>).</summary>
    public abstract class ScreenBase : ViewModelBase
    {
        protected readonly HmiContext Ctx;

        protected ScreenBase(HmiContext ctx) { Ctx = ctx; }

        public HmiContext Context => Ctx;
        public Dictionary<string, IPlcTag> Tags => Ctx.Tags;

        /// <summary>Key used by the navigation rail.</summary>
        public abstract string Key { get; }
        /// <summary>Heading shown in the top bar.</summary>
        public abstract string Title { get; }
    }
}
