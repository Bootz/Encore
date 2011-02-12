﻿using System.Diagnostics.Contracts;
using System.Text;
using Trinity.Core.Cryptography;
using Trinity.Core.Cryptography.SRP;
using Trinity.Core.IO;
using Trinity.Core.Mathematics;
using Trinity.Encore.AuthenticationService.Authentication;
using Trinity.Encore.Game.Cryptography;
using Trinity.Encore.Game.IO;
using Trinity.Encore.Game.Network;
using Trinity.Encore.Game.Network.Handling;
using Trinity.Encore.Game.Network.Transmission;
using Trinity.Encore.Game.Security;
using Trinity.Network.Connectivity;

namespace Trinity.Encore.AuthenticationService.Handlers
{
    public static class AuthenticationHandler
    {
        [AuthPacketHandler(GruntOpCode.AuthenticationLogOnChallenge)]
        public static void HandleAuthLogOnChallenge(IClient client, IncomingAuthPacket packet)
        {
            Contract.Requires(client != null);
            Contract.Requires(packet != null);

            packet.ReadByte(); // unk
            packet.ReadInt16(); // size
            packet.ReadFourCC(); // gameName
            packet.ReadByte(); // version1
            packet.ReadByte(); // version2
            packet.ReadByte(); // version3
            packet.ReadInt16(); // build
            packet.ReadFourCC(); // platform
            packet.ReadFourCC(); // os
            packet.ReadFourCC(); // country
            packet.ReadInt32(); // timeZoneBias
            packet.ReadInt32(); // ip
            var username = packet.ReadP8String();
            Contract.Assume(!string.IsNullOrEmpty(username));
            SRPServer srpData = GetSRPDataForUserName(username);
            if (srpData == null)
            {
                SendAuthenticationChallengeFailure(client, AuthenticationResult.FailedUnknownAccount);
            }
            else
            {
                client.UserData.SRP = srpData;

                // make sure the result is at least 32 bytes long
                var peData = srpData.PublicEphemeralValueB.GetBytes(32);
                var publicEphemeral = new BigInteger(peData);

                var rand = new BigInteger(new FastRandom(), 16 * 8);
                Contract.Assume(srpData.Parameters.Modulus.ByteLength == 32);
                Contract.Assume(srpData.Salt.ByteLength == 32);
                Contract.Assume(rand.ByteLength == 16);
                Contract.Assume(srpData.Parameters.Generator.ByteLength == 1);
                SendAuthenticationChallengeSuccess(client,
                                                   publicEphemeral,
                                                   srpData.Parameters.Generator,
                                                   srpData.Parameters.Modulus,
                                                   srpData.Salt,
                                                   rand);
            }
        }

        /// <summary>
        /// Calculates SRP information based on the username of the account used.
        /// If this account does not exist in the database, return null.
        /// </summary>
        /// <param name="userName">The account's username.</param>
        /// <returns>null if no account exists, otherwise an SRPServer instance to be used for this connection.</returns>
        private static SRPServer GetSRPDataForUserName(string userName)
        {
            Contract.Requires(!string.IsNullOrEmpty(userName));
            // TODO this needs to be fetched from the database
            BigInteger credentials = null ?? new BigInteger(0);
            SRPServer srpData = new SRPServer(userName, credentials, new WowAuthParameters());
            srpData.Salt = new BigInteger(new FastRandom(), 32 * 8);
            return srpData;
        }

        public static void SendAuthenticationChallengeFailure(IClient client, AuthenticationResult result)
        {
            Contract.Requires(client != null);
            Contract.Requires(result != AuthenticationResult.Success);

            using (var packet = new OutgoingAuthPacket(GruntOpCode.AuthenticationLogOnChallenge, 2))
            {
                packet.Write((byte)0x00);
                packet.Write((byte)result);
                client.Send(packet);
            }
        }

        public static void SendAuthenticationChallengeSuccess(IClient client, BigInteger publicEphemeral, BigInteger generator, BigInteger modulus, BigInteger salt, BigInteger rand)
        {
            Contract.Requires(client != null);
            Contract.Requires(publicEphemeral != null);
            Contract.Requires(publicEphemeral.ByteLength == 32);
            Contract.Requires(modulus != null);
            Contract.Requires(modulus.ByteLength == 32);
            Contract.Requires(salt != null);
            Contract.Requires(salt.ByteLength == 32);
            Contract.Requires(rand != null);
            Contract.Requires(rand.ByteLength == 16);
            Contract.Requires(generator != null);
            Contract.Requires(generator.ByteLength == 1);

            using (var packet = new OutgoingAuthPacket(GruntOpCode.AuthenticationLogOnChallenge, 118))
            {
                packet.Write((byte)0x00); // If this is > 0, the client fails immediately
                packet.Write((byte)AuthenticationResult.Success);
                packet.Write(publicEphemeral, 32);
                packet.Write(generator, 1, true);
                packet.Write(modulus, 32, true);
                packet.Write(salt, 32);
                packet.Write(rand, 16); // HMAC seed for the client exe verification (I think)

                var extraSecurityFlags = ExtraSecurityFlags.None;
                packet.Write((byte)extraSecurityFlags);
                if (extraSecurityFlags.HasFlag(ExtraSecurityFlags.Pin))
                {
                    packet.Write(0); // Used as the factor for determining PIN order

                    // this is supposed to be an array of 16 bytes but there's no purpose in initializing it now
                    packet.Write((long)0);
                    packet.Write((long)0);
                }

                if (extraSecurityFlags.HasFlag(ExtraSecurityFlags.Matrix))
                {
                    packet.Write((byte) 0); // height
                    packet.Write((byte) 0); // width
                    packet.Write((byte) 0); // minDigits
                    packet.Write((byte) 0); // maxDigits
                    packet.Write((long) 0); // seed for md5

                    // Client MD5's the seed + sessionkey, and uses it as the seed to an HMAC-SHA1
                    // Client then uses the MD5 as a seed to an RC4 context
                    // On every keypress, the client processes the entered value with the RC4, then updates the HMAC with that value
                    // The HMAC result is sent in the auth proof
                }

                if (extraSecurityFlags.HasFlag(ExtraSecurityFlags.SecurityToken))
                    packet.Write((byte) 0);

                client.Send(packet);
            }
        }

        [AuthPacketHandler(GruntOpCode.AuthenticationLogOnProof)]
        public static void HandleAuthLogOnProof(IClient client, IncomingAuthPacket packet)
        {
            Contract.Requires(client != null);
            Contract.Requires(packet != null);

            var clientPublicEphemeralA = packet.ReadBigInteger(32);
            // Client Proof.
            // SHA1 of { SHA1(Modulus) ^ SHA1(Generator), SHA1(USERNAME), salt, PublicA, PublicB, SessionKey }
            var clientResult = packet.ReadBigInteger(20);
            // SHA1 hash of the PublicA and HMACSHA1 of the contents of WoW.exe and unicows.dll. HMAC seed is the 16 bytes at the end of the challenge sent by the server.
            packet.ReadBytes(20); // these can safely be ignored, clientFileHash

            // the client tends to send 0, but just in case it's safer to implement this.
            var numKeys = packet.ReadByte();
            if (numKeys > 0)
            {
                for (var key = 0; key < numKeys; key++)
                {
                    packet.ReadInt16();
                    packet.ReadInt32();
                    packet.ReadBytes(4);
                    // SHA of { PublicA, PublicB, byte[20] unknown data }
                    packet.ReadBytes(20);
                }
            }

            var securityFlags = (ExtraSecurityFlags)packet.ReadByte(); // can be safely ignored

            if (securityFlags.HasFlag(ExtraSecurityFlags.Pin))
            {
                packet.ReadBytes(16); // pinRandom
                packet.ReadBytes(20); // pinSha1
            }

            if (securityFlags.HasFlag(ExtraSecurityFlags.Matrix))
            {
                packet.ReadBytes(20); // matrixHmacResult
            }

            if (securityFlags.HasFlag(ExtraSecurityFlags.SecurityToken))
            {
                var tokenLength = packet.ReadByte();
                Contract.Assume(tokenLength >= 0);
                packet.ReadBytes(tokenLength); // token
            }

            SRPServer srpData = client.UserData.SRP;
            Contract.Assume(clientPublicEphemeralA != null);
            srpData.PublicEphemeralValueA = clientPublicEphemeralA;
            Contract.Assume(clientResult != null);
            var success = srpData.Validator.IsClientProofValid(clientResult);
            if (success)
            {
                SendAuthenticationLogOnProofSuccess(client, srpData.Validator.ServerSessionKeyProof);
                client.AddPermission(new AuthenticatedPermission());
            }
            else
                SendAuthenticationLogOnProofFailure(client, AuthenticationResult.FailedUnknownAccount);
        }

        public static void SendAuthenticationLogOnProofSuccess(IClient client, BigInteger serverResult)
        {
            Contract.Requires(client != null);
            Contract.Requires(serverResult != null);

            using (var packet = new OutgoingAuthPacket(GruntOpCode.AuthenticationLogOnProof, 31))
            {
                packet.Write((byte)AuthenticationResult.Success);
                packet.Write(serverResult, 20);
                // Flags. Only These are checked
                // 0x1 = ?
                // 0x8 = Trial Account
                // 0x800000 = ?
                packet.Write(0x00800000);
                // If 1, the client will do a hardware survey
                packet.Write(0x00);
                // If 1, client will fire EVENT_ACCOUNT_MESSAGES_AVAILABLE
                packet.Write((short)0x00);
                client.Send(packet);
            }
        }

        public static void SendAuthenticationLogOnProofFailure(IClient client, AuthenticationResult result)
        {
            Contract.Requires(client != null);

            using (var packet = new OutgoingAuthPacket(GruntOpCode.AuthenticationLogOnProof, 3))
            {
                packet.Write((byte)result);
                if (result == AuthenticationResult.FailedUnknownAccount)
                {
                    // This is only read if the result == 4, and even then its not used. But it does need this to be here, as it does a length check before reading
                    packet.Write((short) 0);
                }
                client.Send(packet);
            }
        }

        [AuthPacketHandler(GruntOpCode.AuthenticationReconnectChallenge)]
        public static void HandleReconnectChallenge(IClient client, IncomingAuthPacket packet)
        {
            // structure is the same as AuthenticationLogOnChallenge
            Contract.Requires(client != null);
            Contract.Requires(packet != null);

            packet.ReadByte(); // unk
            packet.ReadInt16(); // size
            packet.ReadFourCC(); // gameName
            packet.ReadByte(); // version1
            packet.ReadByte(); // version2
            packet.ReadByte(); // version3
            packet.ReadInt16(); // build
            packet.ReadFourCC(); // platform
            packet.ReadFourCC(); // os
            packet.ReadFourCC(); // country
            packet.ReadInt32(); // timeZoneBias
            packet.ReadInt32(); // ip
            packet.ReadP8String();

            // TODO fetch this from the database (or some other persistent storage)
            BigInteger sessionKey = null;
            if (sessionKey == null)
            {
                client.Disconnect();
                return;
            }

            //BigInteger rand = new BigInteger(new FastRandom(), 16 * 8);
            //SendReconnectChallengeSuccess(client, rand);
            //client.UserData.ReconnectRand = rand;
            //client.UserData.Username = username;
        }

        private static void SendReconnectChallengeSuccess(IClient client, BigInteger reconnectProof)
        {
            Contract.Requires(client != null);
            Contract.Requires(reconnectProof != null);
            Contract.Requires(reconnectProof.ByteLength == 16);

            using (var packet = new OutgoingAuthPacket(GruntOpCode.AuthenticationReconnectChallenge, 34))
            {
                packet.Write((byte)AuthenticationResult.Success);
                packet.Write(reconnectProof, 16); // ReconnectProof

                // this should be a byte[16] but there's no point in initializing it as we always send 0s
                // Seed for the HMAC for the client exe verification
                packet.Write((ulong)0);
                packet.Write((ulong)0);
            }
        }

        [AuthPacketHandler(GruntOpCode.AuthenticationReconnectProof)]
        public static void HandleReconnectProof(IClient client, IncomingAuthPacket packet)
        {
            Contract.Requires(client != null);
            Contract.Requires(packet != null);

            // MD5 hash of { AccountName, byte[16] random data }
            BigInteger r1 = packet.ReadBigInteger(16);
            // SHA1 hash of { AccountName, MD5 from above, ReconnectProof, SessionKey }
            BigInteger r2 = packet.ReadBigInteger(20);
            // SHA1 hash of { MD5 from above, byte[16] of 0's }
            packet.ReadBigInteger(20); // r3Data

            var numKeys = packet.ReadByte();
            if (numKeys > 0)
            {
                // only initialize the array if we actually HAVE keys
                for (byte key = 0; key < numKeys; key++)
                {
                    packet.ReadInt16();
                    packet.ReadInt32();
                    packet.ReadBytes(4);
                    // SHA of { PublicA, PublicB, byte[20] unknown data }
                    packet.ReadBytes(20);
                }
            }

            SRPServer srpData = client.UserData.SRP;
            string username = client.UserData.Username;
            BigInteger rand = client.UserData.ReconnectRand;

            // TODO fetch this from the database (or some other persistent storage)
            //BigInteger sessionKey = null ?? new BigInteger(0);
            Contract.Assume(username != null);
            Contract.Assume(r1 != null);
            Contract.Assume(rand != null);
            BigInteger hash = srpData.Hash(new HashDataBroker(Encoding.ASCII.GetBytes(username)), r1, rand);
            if (hash == r2)
            {
                SendReconnectProofSuccess(client);
                client.AddPermission(new AuthenticatedPermission());
            }
            else
                client.Disconnect();
        }

        private static void SendReconnectProofSuccess(IClient client)
        {
            using (var packet = new OutgoingAuthPacket(GruntOpCode.AuthenticationReconnectProof, 3))
            {
                packet.Write((byte)AuthenticationResult.Success);
                packet.Write((short)0); // two unknown bytes
                client.Send(packet);
            }
        }
    }
}