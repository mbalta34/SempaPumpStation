using System.Collections.Generic;
using TerakkiFramework.Plc.Abstractions;

namespace SempaPumpStation.Infrastructure
{
    /// <summary>
    /// A prefix view on the tag dictionary, so one view can serve every zone or position:
    /// <c>{Binding Ph[Flow].TagValue}</c> resolves to "StationPh.Zone2.Flow" on zone 2.
    /// </summary>
    public sealed class TagScope
    {
        private readonly Dictionary<string, IPlcTag> _tags;

        public TagScope(Dictionary<string, IPlcTag> tags, string prefix)
        {
            _tags = tags;
            Prefix = prefix;
        }

        public string Prefix { get; }

        public IPlcTag this[string key]
        {
            get
            {
                IPlcTag t;
                return _tags.TryGetValue(Prefix + key, out t) ? t : null;
            }
        }
    }
}
