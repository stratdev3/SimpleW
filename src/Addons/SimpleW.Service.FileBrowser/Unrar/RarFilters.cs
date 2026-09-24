using System.Buffers.Binary;


namespace SimpleW.Service.FileBrowser.Unrar {

    /// <summary>
    /// Reverses the standard RAR data filters without modifying the LZ dictionary.
    /// </summary>
    internal static class RarFilters {

        #region Filter dispatch

        // Types 0..3 are the RAR5 wire numbers; 4..6 identify legacy standard programs.
        /// <summary>
        /// Reverses a supported standard filter, returning a block of the same length that may reuse the input
        /// array.
        /// </summary>
        internal static byte[] Apply(int type, byte[] data, long fileOffset, int parameter, int second, bool legacy) {
            if (type == 0) {
                RarException.Require(parameter is >= 1 and <= 1024, "Invalid RAR delta channel count.");
                byte[] decoded = new byte[data.Length];
                int source = 0;
                for (int channel = 0; channel < parameter; channel++) {
                    byte previous = 0;
                    for (int i = channel; i < data.Length; i += parameter)
                        decoded[i] = previous = unchecked((byte)(previous - data[source++]));
                }
                return decoded;
            }
            if (type is 1 or 2) {
                const uint fileSize = 1 << 24;
                for (int i = 0; i + 4 < data.Length; i++) {
                    if (data[i] != 0xE8 && (type != 2 || data[i] != 0xE9))
                        continue;
                    i++;
                    uint address = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i));
                    uint position = unchecked((uint)(fileOffset + i));
                    if (!legacy)
                        position &= fileSize - 1;
                    if (unchecked((int)address) < 0) {
                        if (unchecked((int)(address + position)) >= 0)
                            address = unchecked(address + fileSize);
                    }
                    else if (address < fileSize)
                        address = unchecked(address - position);
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i), address);
                    i += 3;
                }
                return data;
            }
            if (type == 3) {
                for (int i = 0; i + 3 < data.Length; i += 4) {
                    if (data[i + 3] != 0xEB)
                        continue;
                    uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i));
                    value = unchecked(value - (uint)((fileOffset + i) >> 2));
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i), (value & 0xFFFFFF) | 0xEB000000);
                }
                return data;
            }
            if (type == 4)
                return Rgb(data, parameter, second);
            if (type == 5)
                return Audio(data, parameter);
            if (type == 6)
                return Itanium(data, fileOffset);
            throw RarException.Unsupported("Unknown RAR filter.");
        }

        #endregion

        #region Legacy transformations

        /// <summary>
        /// Restores legacy RGB channel predictions and adds the green channel back into red and blue.
        /// </summary>
        private static byte[] Rgb(byte[] source, int width, int red) {
            RarException.Require(width >= 3 && width <= source.Length + 3L && red is >= 0 and <= 2, "Invalid RAR RGB filter parameters.");
            byte[] output = new byte[source.Length];
            width -= 3;
            int cursor = 0;
            for (int channel = 0; channel < 3; channel++) {
                int previous = 0;
                for (int i = channel; i < output.Length; i += 3) {
                    int prediction = previous, upperPosition = i - width;
                    if (upperPosition >= 3) {
                        int upper = output[upperPosition], upperLeft = output[upperPosition - 3];
                        prediction = previous + upper - upperLeft;
                        int dl = Math.Abs(prediction - previous), du = Math.Abs(prediction - upper), dc = Math.Abs(prediction - upperLeft);
                        prediction = dl <= du && dl <= dc ? previous : du <= dc ? upper : upperLeft;
                    }
                    output[i] = (byte)(previous = unchecked((byte)(prediction - source[cursor++])));
                }
            }
            for (int i = red; i + 2 < output.Length; i += 3) {
                output[i] = unchecked((byte)(output[i] + output[i + 1]));
                output[i + 2] = unchecked((byte)(output[i + 2] + output[i + 1]));
            }
            return output;
        }

        /// <summary>
        /// Restores interleaved audio samples using each channel's adaptive predictor.
        /// </summary>
        private static byte[] Audio(byte[] source, int channels) {
            RarException.Require(channels is >= 1 and <= 128, "Invalid RAR audio filter channel count.");
            byte[] result = new byte[source.Length];
            int cursor = 0;
            for (int channel = 0; channel < channels; channel++) {
                int previous = 0, delta = 0, d1 = 0, d2 = 0, d3 = 0, k1 = 0, k2 = 0, k3 = 0, count = 0;
                var errors = new long[7];
                for (int i = channel; i < result.Length; i += channels, count++) {
                    d3 = d2;
                    d2 = delta - d1;
                    d1 = delta;
                    int encoded = source[cursor++];
                    int prediction = ((8 * previous + k1 * d1 + k2 * d2 + k3 * d3) >> 3) & 255;
                    byte value = unchecked((byte)(prediction - encoded));
                    result[i] = value;
                    delta = unchecked((sbyte)(value - previous));
                    previous = value;
                    int error = unchecked((sbyte)encoded) << 3;
                    errors[0] += Math.Abs(error);
                    errors[1] += Math.Abs(error - d1);
                    errors[2] += Math.Abs(error + d1);
                    errors[3] += Math.Abs(error - d2);
                    errors[4] += Math.Abs(error + d2);
                    errors[5] += Math.Abs(error - d3);
                    errors[6] += Math.Abs(error + d3);
                    if ((count & 31) == 0) {
                        int best = 0;
                        for (int e = 1; e < 7; e++)
                        if (errors[e] < errors[best])
                            best = e;
                        if (best == 1 && k1 >= -16)
                            k1--;
                        if (best == 2 && k1 < 16)
                            k1++;
                        if (best == 3 && k2 >= -16)
                            k2--;
                        if (best == 4 && k2 < 16)
                            k2++;
                        if (best == 5 && k3 >= -16)
                            k3--;
                        if (best == 6 && k3 < 16)
                            k3++;
                        Array.Clear(errors);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Restores relative branch targets in eligible slots of 16-byte Itanium instruction bundles.
        /// </summary>
        private static byte[] Itanium(byte[] data, long fileOffset) {
            ReadOnlySpan<byte> slots = [4, 4, 6, 6, 0, 0, 7, 7, 4, 4, 0, 0, 4, 4, 0, 0];
            for (int start = 0; start + 16 <= data.Length; start += 16) {
                int template = (data[start] & 31) - 16;
                if (template < 0)
                    continue;
                for (int slot = 0; slot < 3; slot++) {
                    if ((slots[template] & (1 << slot)) == 0)
                        continue;
                    int bit = 5 + 41 * slot;
                    if (ReadField(data, start, bit + 37, 4) != 5)
                        continue;
                    uint address = ReadField(data, start, bit + 13, 20);
                    address = unchecked(address - (uint)((fileOffset + start) >> 4)) & 0xFFFFF;
                    for (int n = 0; n < 20; n++) {
                        int target = bit + 13 + n, index = start + target / 8, mask = 1 << (target & 7);
                        data[index] = (byte)((data[index] & ~mask) | (((address >> n) & 1) != 0 ? mask : 0));
                    }
                }
            }
            return data;
        }

        #endregion

        #region Instruction bit fields

        /// <summary>
        /// Reads a little-endian bit field from an instruction bundle at the supplied byte offset.
        /// </summary>
        private static uint ReadField(byte[] data, int start, int bit, int count) {
            uint result = 0;
            for (int n = 0; n < count; n++)
                result |= (uint)((data[start + (bit + n) / 8] >> ((bit + n) & 7)) & 1) << n;
            return result;
        }

        #endregion
    }

    /// <summary>
    /// Recognizes standard RAR4 filter programs and schedules them without executing arbitrary VM code.
    /// </summary>
    /// <param name="maximum">Maximum number of remembered standard filter programs.</param>
    internal sealed class Rar4Filters(int maximum) {

        #region Supporting types

        /// <summary>
        /// Remembers a recognized standard filter and its most recently requested block length.
        /// </summary>
        /// <param name="Type">Recognized standard filter identifier.</param>
        /// <param name="Length">Most recently used filter block length in bytes.</param>
        private sealed record Program(int Type, int Length);

        #endregion

        #region State

        /// <summary>
        /// Stores recognized standard filters and the last block length associated with each program index.
        /// </summary>
        private readonly List<Program> programs = new();

        /// <summary>
        /// Stores the program index reused when the next filter record omits an explicit selector.
        /// </summary>
        private int previous;

        #endregion

        #region Program lifecycle

        /// <summary>
        /// Discards remembered programs and resets the implicit program selector.
        /// </summary>
        internal void Reset() { programs.Clear(); previous = 0; }

        #endregion

        #region Filter records

        /// <summary>
        /// Parses a filter record, validates its program fingerprint and schedules the corresponding standard
        /// transformation.
        /// </summary>
        internal void Read(Func<byte> readByte, RarWindow window, CancellationToken cancellation) {
            int flags = readByte(), count = (flags & 7) + 1;
            if (count == 7)
                count = readByte() + 7;
            else if (count == 8)
                count = (readByte() << 8) | readByte();
            RarException.Require(count > 0, "Empty RAR filter record.");
            var payload = new byte[count];
            for (int i = 0; i < count; i++)
                payload[i] = readByte();
            using var stream = new MemoryStream(payload, false);
            var bits = new RarBits(stream, cancellation) { Limit = count * 8L };
            int index = previous;
            if ((flags & 128) != 0) {
                uint number = Integer(bits);
                RarException.Require(number <= maximum, "RAR filter index exceeds its limit.");
                if (number == 0) { Reset(); index = 0; }
                else
                    index = (int)number - 1;
            }
            RarException.Require(index <= programs.Count, "RAR filter refers to an unknown program.");
            bool fresh = index == programs.Count;
            if (fresh && programs.Count >= maximum)
                throw new RarException(RarFailure.Limit, "Too many RAR filter programs.");
            long start = Integer(bits);
            if ((flags & 64) != 0)
                start += 258;
            uint length = (flags & 32) != 0 ? Integer(bits) : fresh ? 0 : (uint)programs[index].Length;
            RarException.Require(length is > 0 and <= 0x3C000, "Invalid RAR4 filter block length.");
            var registers = new uint[7];
            registers[3] = 0x3C000;
            registers[4] = length;
            if ((flags & 16) != 0) {
                int mask = (int)bits.Read(7);
                for (int i = 0; i < 7; i++)
                if ((mask & (1 << i)) != 0)
                    registers[i] = Integer(bits);
            }
            int type;
            if (fresh) {
                uint size = Integer(bits);
                RarException.Require(size is > 0 and <= 65535, "Invalid RAR filter program length.");
                byte[] code = new byte[(int)size];
                int xor = 0;
                for (int i = 0; i < code.Length; i++) { code[i] = (byte)bits.Read(8); xor ^= code[i]; }
                RarException.Require(xor == 0, "RAR filter program checksum mismatch.");
                type = (code.Length, RarCrc.Compute(code)) switch {
                    (53, 0xAD576887) => 1,
                    (57, 0x3CD7E57E) => 2,
                    (120, 0x3769893F) => 6,
                    (29, 0x0E06077D) => 0,
                    (149, 0x1C2C5DC8) => 4,
                    (216, 0xBC85E701) => 5,
                    _ => throw RarException.Unsupported("Custom RAR virtual-machine programs are not supported.")
                };
                programs.Add(new(type, (int)length));
            }
            else { type = programs[index].Type; programs[index] = new(type, (int)length); }
            if ((flags & 8) != 0) {
                uint globalSize = Integer(bits);
                RarException.Require(globalSize <= 0x2000 - 64, "RAR filter global-data limit exceeded.");
                for (uint i = 0; i < globalSize; i++)
                    bits.Read(8);
            }
            previous = index;
            int parameter = checked((int)registers[0]), second = checked((int)registers[1]);
            window.Schedule(new(checked(window.Produced + start), (int)length,
                (data, offset) => RarFilters.Apply(type, data, offset, parameter, second, true)), true);
        }

        #endregion

        #region Encoded parameters

        /// <summary>
        /// Reads the RAR VM compact integer encoding, including its sign-extended short form.
        /// </summary>
        private static uint Integer(RarBits bits) {
            uint mode = bits.Read(2);
            if (mode == 0)
                return bits.Read(4);
            if (mode == 2)
                return bits.Read(16);
            if (mode == 3)
                return bits.Read(32);
            uint value = bits.Read(8);
            return value >= 16 ? value : 0xFFFFFF00 | (value << 4) | bits.Read(4);
        }

        #endregion
    }

}