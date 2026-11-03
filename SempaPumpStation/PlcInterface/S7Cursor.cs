using TerakkiFramework.Communication.Abstractions;
using TerakkiFramework.Plc;
using TerakkiFramework.Plc.Siemens;

namespace SempaPumpStation.PlcInterface
{
    /// <summary>
    /// Walks a non-optimized (standard access) S7 struct member by member and hands out each member's
    /// address, using the same rules as <see cref="S7Layout"/>: BOOLs bit-pack, wider scalars start on an
    /// even byte, STRINGs / structs / arrays start on an even byte, and a struct occupies
    /// <see cref="S7Layout.SizeOf"/> bytes. The generated tag models use it instead of literal offsets,
    /// so a change in a UDT reflows every offset after it.
    /// </summary>
    public sealed class S7Cursor
    {
        private readonly int _db;
        private readonly IConnectionService _connection;
        private readonly WriteDispatchMode _writeMode;
        private readonly ReadPlan _readPlan;
        private int _bit;

        public S7Cursor(IConnectionService connection, int db, int byteOffset, WriteDispatchMode writeMode, ReadPlan readPlan)
        {
            _connection = connection;
            _db = db;
            _bit = byteOffset * 8;
            _writeMode = writeMode;
            _readPlan = readPlan;
        }

        /// <summary>Offset of the next struct / array element (even byte).</summary>
        public int Struct()
        {
            AlignEven();
            return _bit / 8;
        }

        /// <summary>Account for a struct created at <see cref="Struct"/>.</summary>
        public T Next<T>(T tag) where T : SiemensS7PlcTag
        {
            _bit += S7Layout.SizeOf(tag) * 8;
            return tag;
        }

        /// <summary>Arrays start on an even byte.</summary>
        public void ArrayStart() => AlignEven();

        public SiemensS7PlcTag Leaf(string name, DataType type, int stringMaxLength = 0)
        {
            string address;
            switch (type)
            {
                case DataType.BOOL:
                case DataType.MOMENTARY_BOOL:
                    address = "DB" + _db + ".DBX" + (_bit / 8) + "." + (_bit % 8);
                    _bit += 1;
                    break;
                case DataType.SINT:
                case DataType.USINT:
                case DataType.CHAR:
                    AlignByte();
                    address = "DB" + _db + ".DBX" + (_bit / 8) + ".0";
                    _bit += 8;
                    break;
                default:
                    AlignEven();
                    address = "DB" + _db + ".DBX" + (_bit / 8) + ".0";
                    _bit += Bytes(type, stringMaxLength) * 8;
                    break;
            }
            return type == DataType.STRING
                ? new SiemensS7PlcTag(_connection, name, address, type, _writeMode, _readPlan, stringMaxLength)
                : new SiemensS7PlcTag(_connection, name, address, type, _writeMode, _readPlan);
        }

        private static int Bytes(DataType type, int stringMaxLength)
        {
            switch (type)
            {
                case DataType.INT:
                case DataType.UINT:
                    return 2;
                case DataType.DINT:
                case DataType.UDINT:
                case DataType.REAL:
                    return 4;
                case DataType.STRING:
                    return 2 + stringMaxLength;
                default:
                    throw new System.NotSupportedException("S7Cursor: unsupported leaf type " + type);
            }
        }

        private void AlignByte() => _bit = (_bit + 7) / 8 * 8;

        private void AlignEven()
        {
            AlignByte();
            if ((_bit / 8) % 2 == 1) _bit += 8;
        }
    }

    /// <summary>The S7 DTL system type (12 bytes), as a struct so it lays out like the PLC's.</summary>
    public class DTL : SiemensS7PlcTag
    {
        public DTL(IConnectionService c, string name, int db, int byteOffset, WriteDispatchMode w, ReadPlan r)
            : base(c, name, "DB" + db + ".DBB" + byteOffset, DataType.STRUCT, w, r)
        {
            var k = new S7Cursor(c, db, byteOffset, w, r);
            Year = k.Leaf(name + ".Year", DataType.UINT);
            Month = k.Leaf(name + ".Month", DataType.USINT);
            Day = k.Leaf(name + ".Day", DataType.USINT);
            Weekday = k.Leaf(name + ".Weekday", DataType.USINT);
            Hour = k.Leaf(name + ".Hour", DataType.USINT);
            Minute = k.Leaf(name + ".Minute", DataType.USINT);
            Second = k.Leaf(name + ".Second", DataType.USINT);
            Nanosecond = k.Leaf(name + ".Nanosecond", DataType.UDINT);
        }

        public SiemensS7PlcTag Year { get; private set; }
        public SiemensS7PlcTag Month { get; private set; }
        public SiemensS7PlcTag Day { get; private set; }
        public SiemensS7PlcTag Weekday { get; private set; }
        public SiemensS7PlcTag Hour { get; private set; }
        public SiemensS7PlcTag Minute { get; private set; }
        public SiemensS7PlcTag Second { get; private set; }
        public SiemensS7PlcTag Nanosecond { get; private set; }
    }
}
