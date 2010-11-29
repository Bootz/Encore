using System.Diagnostics.Contracts;
using Trinity.Encore.Framework.Network.Transmission;

namespace Trinity.Encore.Framework.Game.Network.Transmission
{
    public sealed class IncomingAuthPacket : IncomingPacket
    {
        public IncomingAuthPacket(GruntOpCodes opCode, byte[] data, int length)
            : base(opCode, data, length, Defines.Protocol.Encoding)
        {
            Contract.Requires(data != null);
            Contract.Requires(length >= 0);
            Contract.Requires(length <= data.Length);
        }

        public new GruntOpCodes OpCode
        {
            get { return (GruntOpCodes)base.OpCode; }
        }
    }
}