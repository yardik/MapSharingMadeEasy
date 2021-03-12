using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using UnityEngine;

namespace MapSharingMadeEasy
{
    public class Utils
    {
        public static int GetStableHashCode(string str)
        {
            int num1 = 5381;
            int num2 = num1;
            for (int index = 0; index < str.Length && str[index] != char.MinValue; index += 2)
            {
                num1 = (num1 << 5) + num1 ^ (int) str[index];
                if (index != str.Length - 1 && str[index + 1] != char.MinValue)
                    num2 = (num2 << 5) + num2 ^ (int) str[index + 1];
                else
                    break;
            }

            return num1 + num2 * 1566083941;
        }
        

        public static string DecompressString(string compressedText)
        {
            var gZipBuffer = Convert.FromBase64String(compressedText);
            using (var memoryStream = new MemoryStream())
            {
                var dataLength = BitConverter.ToInt32(gZipBuffer, 0);
                memoryStream.Write(gZipBuffer, 4, gZipBuffer.Length - 4);

                var buffer = new byte[dataLength];

                memoryStream.Position = 0;
                using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                {
                    gZipStream.Read(buffer, 0, buffer.Length);
                }

                return Encoding.UTF8.GetString(buffer);
            }
        }

        public static bool AddMPieceToPieceTable(List<GameObject> pieces, GameObject toAdd)
        {
            Piece piece = toAdd.GetComponent<Piece>();
            var added = false;
            for (var i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].GetComponent<Piece>().m_name == piece.m_name)
                {
                    added = true;
                    break;
                }
            }

            if (!added)
            {
                Debug.Log($"Adding missing recipe to pieces");
                pieces.Add(toAdd);
                return true;
            }

            return false;
        }
    }
}