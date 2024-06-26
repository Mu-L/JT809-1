﻿using JT809.Protocol.Buffers;
using JT809.Protocol.Exceptions;
using JT809.Protocol.Extensions;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;


namespace JT809.Protocol.MessagePack
{
    public ref struct JT809MessagePackReader
    {
        public ReadOnlySpan<byte> Reader { get; private set; }
        public ReadOnlySpan<byte> SrcBuffer { get; }
        public int ReaderCount { get; private set; }
        private ushort _calculateCheckCRCCode;
        private ushort _realCheckCRCCode;
        private bool _checkCRCCodeVali;
        /// <summary>
        /// 是否进行解码操作
        /// 若进行解码操作，则对应的是一个正常的包
        /// 若不进行解码操作，则对应的是一个非正常的包（头部包，数据体包等等）
        /// 主要用来一次性读取所有数据体内容操作
        /// </summary>
        private bool _decoded;
        private static byte[] decode5a01 = new byte[] { 0x5a, 0x01 };
        private static byte[] decode5a02 = new byte[] { 0x5a, 0x02 };
        private static byte[] decode5e01 = new byte[] { 0x5e, 0x01 };
        private static byte[] decode5e02 = new byte[] { 0x5e, 0x02 };
        /// <summary>
        /// 解码（转义还原）,计算校验和
        /// </summary>
        /// <param name="buffer"></param>
        public JT809MessagePackReader(ReadOnlySpan<byte> buffer)
        {
            SrcBuffer = buffer;
            ReaderCount = 0;
            _realCheckCRCCode = 0x00;
            _calculateCheckCRCCode = 0xFFFF;
            _checkCRCCodeVali = false;
            _decoded = false;
            Reader = buffer;
        }
        /// <summary>
        /// 在解码的时候把校验和也计算出来，避免在循环一次进行校验
        /// </summary>
        /// <returns></returns>
        public void Decode()
        {
            Span<byte> span = new byte[SrcBuffer.Length];
            Decode(span);
            _decoded = true;
        }
        /// <summary>
        /// 在解码的时候把校验和也计算出来，避免在循环一次进行校验
        /// </summary>
        /// <returns></returns>
        public void Decode(Span<byte> allocateBuffer)
        {
            int offset = 0;
            var checkcodeOffset = SrcBuffer.Length - 3;
            for (int i = 0; i < SrcBuffer.Length; i++)
            {
                if (TryDecode(SrcBuffer.Slice(i, SrcBuffer.Length - i < 2 ? 1 : 2), out byte tmp))
                {
                    i++;
                    checkcodeOffset--;
                }
                if (offset > 0 && offset < checkcodeOffset)
                {
                    _calculateCheckCRCCode = (ushort)((_calculateCheckCRCCode << 8) ^ (ushort)CRCUtil.CRC[(_calculateCheckCRCCode >> 8) ^ tmp]);
                }
                allocateBuffer[offset++] = tmp;
            }
            Reader = allocateBuffer.Slice(0, offset);
            _realCheckCRCCode = (ushort)((Reader[Reader.Length - 3] << 8) | Reader[Reader.Length - 2]);
            _checkCRCCodeVali = _calculateCheckCRCCode == _realCheckCRCCode;
            _decoded = true;
        }

        public void FullDecode()
        {
            int offset = 0;
            Span<byte> span = new byte[SrcBuffer.Length];
            int len = SrcBuffer.Length;
            for (int i = 0; i < len; i++)
            {
                byte tmp;
                if ((SrcBuffer.Length - i) >= 2)
                {
                    if (TryDecode(SrcBuffer.Slice(i, 2), out tmp))
                    {
                        i++;
                    }
                }
                else
                {
                    tmp = SrcBuffer[i];
                }
                span[offset++] = tmp;
            }
            Reader = span.Slice(0, offset);
        }

        private bool TryDecode(ReadOnlySpan<byte> buffer, out byte value)
        {
            if (buffer.SequenceEqual(decode5a01))
            {
                value = 0x5b;
                return true;
            }
            else if (buffer.SequenceEqual(decode5a02))
            {
                value = 0x5a;
                return true;
            }
            else if (buffer.SequenceEqual(decode5e01))
            {
                value = 0x5d;
                return true;
            }
            else if (buffer.SequenceEqual(decode5e02))
            {
                value = 0x5e;
                return true;
            }
            else
            {
                value = buffer[0];
                return false;
            }
        }
        public ushort CalculateCheckXorCode => _calculateCheckCRCCode;
        public ushort RealCheckXorCode => _realCheckCRCCode;
        public bool CheckXorCodeVali => _checkCRCCodeVali;
        public byte ReadStart() => ReadByte();
        public byte ReadEnd() => ReadByte();
        public ushort ReadUInt16()
        {
            return BinaryPrimitives.ReadUInt16BigEndian(GetReadOnlySpan(2));
        }
        public ushort ReadUInt16(ReadOnlySpan<byte> buffer)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(0, 2));
        }
        public uint ReadUInt32()
        {
            return BinaryPrimitives.ReadUInt32BigEndian(GetReadOnlySpan(4));
        }
        public int ReadInt32()
        {
            return BinaryPrimitives.ReadInt32BigEndian(GetReadOnlySpan(4));
        }
        public ulong ReadUInt64()
        {
            return BinaryPrimitives.ReadUInt64BigEndian(GetReadOnlySpan(8));
        }
        public long ReadInt64()
        {
            return BinaryPrimitives.ReadInt64BigEndian(GetReadOnlySpan(8));
        }
        public ReadOnlySpan<byte> ReadVirtualArray(int count)
        {
            return GetVirtualReadOnlySpan(count);
        }
        public byte ReadByte()
        {
            return GetReadOnlySpan(1)[0];
        }
        public byte ReadVirtualByte()
        {
            return GetVirtualReadOnlySpan(1)[0];
        }
        public ushort ReadVirtualUInt16()
        {
            return BinaryPrimitives.ReadUInt16BigEndian(GetVirtualReadOnlySpan(2));
        }
        public short ReadVirtualInt16()
        {
            return BinaryPrimitives.ReadInt16BigEndian(GetVirtualReadOnlySpan(2));
        }
        public uint ReadVirtualUInt32()
        {
            return BinaryPrimitives.ReadUInt32BigEndian(GetVirtualReadOnlySpan(4));
        }
        /// <summary>
        /// 使用场景:在不改变现有的结构的情况下进行补救,主要用于获取父类的解析长度在子类获取不到  骚操作
        /// </summary>
        /// <param name="backwordOffset">回退偏移量</param>
        /// <returns></returns>
        public uint ReadVirtualUInt32(int backwordOffset)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(GetVirtualReadOnlySpan(backwordOffset, 4));
        }

        public int ReadVirtualInt32()
        {
            return BinaryPrimitives.ReadInt32BigEndian(GetVirtualReadOnlySpan(4));
        }
        public ulong ReadVirtualUInt64()
        {
            return BinaryPrimitives.ReadUInt64BigEndian(GetVirtualReadOnlySpan(8));
        }
        public long ReadVirtualInt64()
        {
            return BinaryPrimitives.ReadInt64BigEndian(GetVirtualReadOnlySpan(8));
        }
        /// <summary>
        /// 数字编码 大端模式、高位在前
        /// </summary>
        /// <param name="len"></param>
        public string ReadBigNumber(int len)
        {
            ulong result = 0;
            var readOnlySpan = GetReadOnlySpan(len);
            for (int i = 0; i < len; i++)
            {
                ulong currentData = (ulong)readOnlySpan[i] << (8 * (len - i - 1));
                result += currentData;
            }
            return result.ToString();
        }
        public ReadOnlySpan<byte> ReadArray(int len)
        {
            return GetReadOnlySpan(len).Slice(0, len);
        }
        public ReadOnlySpan<byte> ReadArray(int start, int end)
        {
            return Reader.Slice(start, end);
        }
        public string ReadString(int len)
        {
            var readOnlySpan = GetReadOnlySpan(len);
            string value = JT809Constants.Encoding.GetString(readOnlySpan.Slice(0, len).ToArray());
            return value.Trim('\0');
        }
        public string ReadRemainStringContent()
        {
            var readOnlySpan = ReadContent(0);
            string value = JT809Constants.Encoding.GetString(readOnlySpan.ToArray());
            return value.Trim('\0');
        }
        public string ReadHex(int len)
        {
            var readOnlySpan = GetReadOnlySpan(len);
            string hex = HexUtil.DoHexDump(readOnlySpan, 0, len);
            return hex;
        }
        /// <summary>
        /// yyMMddHHmmss
        /// </summary>
        /// <param name="fromBase">>D2： 10  X2：16</param>
        public DateTime ReadDateTime6(string fromBase = "X2")
        {
            DateTime d;
            try
            {
                var readOnlySpan = GetReadOnlySpan(6);
                int year = Convert.ToInt32(readOnlySpan[0].ToString(fromBase)) + JT809Constants.DateLimitYear;
                int month = Convert.ToInt32(readOnlySpan[1].ToString(fromBase));
                int day = Convert.ToInt32(readOnlySpan[2].ToString(fromBase));
                int hour = Convert.ToInt32(readOnlySpan[3].ToString(fromBase));
                int minute = Convert.ToInt32(readOnlySpan[4].ToString(fromBase));
                int second = Convert.ToInt32(readOnlySpan[5].ToString(fromBase));
                d = new DateTime(year, month, day, hour, minute, second);
            }
            catch (Exception)
            {
                d = JT809Constants.UTCBaseTime;
            }
            return d;
        }
        /// <summary>
        /// HH-mm-ss-msms
        /// HH-mm-ss-fff
        /// </summary>
        /// <param name="format">D2： 10  X2：16</param>
        public DateTime ReadDateTime5(string format = "X2")
        {
            DateTime d;
            try
            {
                var readOnlySpan = GetReadOnlySpan(5);
                d = new DateTime(
                DateTime.Now.Year,
                DateTime.Now.Month,
                DateTime.Now.Day,
                Convert.ToInt32(readOnlySpan[0].ToString(format)),
                Convert.ToInt32(readOnlySpan[1].ToString(format)),
                Convert.ToInt32(readOnlySpan[2].ToString(format)),
                Convert.ToInt32(((readOnlySpan[3] << 8) + readOnlySpan[4])));
            }
            catch
            {
                d = JT809Constants.UTCBaseTime;
            }
            return d;
        }
        /// <summary>
        /// YYYYMMDD
        /// </summary>
        /// <param name="format">D2： 10  X2：16</param>
        public DateTime ReadDateTime4(string format = "X2")
        {
            DateTime d;
            try
            {
                var readOnlySpan = GetReadOnlySpan(4);
                d = new DateTime(
               (Convert.ToInt32(readOnlySpan[0].ToString(format)) << 8) + Convert.ToByte(readOnlySpan[1]),
                Convert.ToInt32(readOnlySpan[2].ToString(format)),
                Convert.ToInt32(readOnlySpan[3].ToString(format)));
            }
            catch (Exception)
            {
                d = JT809Constants.UTCBaseTime;
            }
            return d;
        }
        public DateTime ReadUTCDateTime()
        {
            DateTime d;
            try
            {
                d = JT809Constants.UTCBaseTime.AddSeconds(ReadUInt64()).AddHours(8);
            }
            catch (Exception)
            {
                d = JT809Constants.UTCBaseTime;
            }
            return d;
        }
        public string ReadBCD(int len)
        {
            int count = len / 2;
            var readOnlySpan = GetReadOnlySpan(count);
            StringBuilder bcdSb = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                bcdSb.Append(readOnlySpan[i].ToString("X2"));
            }
            return bcdSb.ToString();
        }
        private ReadOnlySpan<byte> GetReadOnlySpan(int count)
        {
            ReaderCount += count;
            return Reader.Slice(ReaderCount - count);
        }
        public ReadOnlySpan<byte> GetVirtualReadOnlySpan(int count)
        {
            return Reader.Slice(ReaderCount, count);
        }
        public ReadOnlySpan<byte> GetVirtualReadOnlySpan(int backwordOffset, int count)
        {
            if (ReaderCount - backwordOffset < 0)
            {
                //处理直接子类导致溢出
                return Reader.Slice(ReaderCount, count);
            }
            return Reader.Slice(ReaderCount - backwordOffset, count);
        }
        public ReadOnlySpan<byte> ReadContent(int count = 0)
        {
            if (_decoded)
            {
                //内容长度=总长度-读取的长度-3（校验码1位+终止符1位）
                int totalContent = Reader.Length - ReaderCount - 3;
                //实际读取内容长度
                int realContent = totalContent - count;
                int tempReaderCount = ReaderCount;
                ReaderCount += realContent;
                return Reader.Slice(tempReaderCount, realContent);
            }
            else
            {
                return Reader.Slice(ReaderCount);
            }
        }
        public int ReadCurrentRemainContentLength()
        {
            int len = 0;
            if (_decoded)
            {
                len = Reader.Length - ReaderCount - 3;
                //内容长度=总长度-读取的长度-3（校验码2位+终止符1位）
            }
            else
            {
                len = Reader.Length - ReaderCount;
            }
            if (len < 0) throw new JT809Exception(Enums.JT809ErrorCode.ReaderRemainContentLengthError);
            return len;
        }
        public void Skip(int count = 1)
        {
            ReaderCount += count;
        }
    }
}
