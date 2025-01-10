using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SolicitSubscriptionEncryptionTool.Helpers
{
	internal class AesHelper
	{
		public static string Encrypt(string key, string input)
		{
			byte[] keyBytes = Encoding.UTF8.GetBytes(key[..32]);
			using var aesAlg = Aes.Create();
			aesAlg.Key = keyBytes;
			aesAlg.IV = Encoding.UTF8.GetBytes(key[..16]);

			ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
			using MemoryStream msEncrypt = new();
			using CryptoStream csEncrypt = new(msEncrypt, encryptor, CryptoStreamMode.Write);
			using (StreamWriter swEncrypt = new(csEncrypt))
			{
				swEncrypt.Write(input);
			}
			byte[] bytes = msEncrypt.ToArray();
			return ByteArrayToHexString(bytes);
		}
		public static string Decrypt(string key, string input)
		{
			byte[] keyBytes = Encoding.UTF8.GetBytes(key[..32]);
			using var aesAlg = Aes.Create();
			aesAlg.Key = keyBytes;
			aesAlg.IV = Encoding.UTF8.GetBytes(key[..16]);

			ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
			using MemoryStream msEncrypt = new MemoryStream();
			using CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
			using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
			{
				swEncrypt.Write(input);
			}
			byte[] bytes = msEncrypt.ToArray();
			return ByteArrayToHexString(bytes);
		}
		public static string AesEncrypt(string input)
		{
			byte[] keyBytes = Encoding.UTF8.GetBytes(AppConst.AesKey.Substring(0, 32));
			using var aesAlg = Aes.Create();
			aesAlg.Key = keyBytes;
			aesAlg.IV = Encoding.UTF8.GetBytes(AppConst.AesKey.Substring(0, 16));

			ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
			using MemoryStream msEncrypt = new MemoryStream();
			using CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
			using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
			{
				swEncrypt.Write(input);
			}
			byte[] bytes = msEncrypt.ToArray();
			return ByteArrayToHexString(bytes);
		}

		/// <summary>  
		/// AES解密  
		/// </summary>  
		/// <param name="input">密文字节数组</param>  
		/// <returns>返回解密后的字符串</returns>  
		public static string AesDecrypt(string input)
		{
			byte[] inputBytes = HexStringToByteArray(input);
			byte[] keyBytes = Encoding.UTF8.GetBytes(AppConst.AesKey.Substring(0, 32));
			using (var aesAlg = Aes.Create())
			{
				aesAlg.Key = keyBytes;
				aesAlg.IV = Encoding.UTF8.GetBytes(AppConst.AesKey.Substring(0, 16));

				ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
				using (MemoryStream msEncrypt = new MemoryStream(inputBytes))
				{
					using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, decryptor, CryptoStreamMode.Read))
					{
						using (StreamReader srEncrypt = new StreamReader(csEncrypt))
						{
							return srEncrypt.ReadToEnd();
						}
					}
				}
			}
		}

		public static byte[] AesEncrypt(byte[] inputBytes)
		{
			byte[] keyBytes = Encoding.UTF8.GetBytes(AppConst.AesKey.Substring(0, 32));
			using var aesAlg = Aes.Create();
			aesAlg.Key = keyBytes;
			aesAlg.IV = Encoding.UTF8.GetBytes(AppConst.AesKey.Substring(0, 16));
			aesAlg.Padding = PaddingMode.PKCS7;
			aesAlg.Mode = CipherMode.CBC;

			ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
			using MemoryStream msEncrypt = new MemoryStream();
			using CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
			csEncrypt.Write(inputBytes, 0, inputBytes.Length);
			csEncrypt.FlushFinalBlock();
			return msEncrypt.ToArray();
		}
		public static byte[] AesDecrypt(byte[] encryptedBuffer)
		{
			byte[] keyBytes = Encoding.UTF8.GetBytes(AppConst.AesKey.Substring(0, 32));
			using var aesAlg = Aes.Create();
			aesAlg.Key = keyBytes;
			aesAlg.IV = Encoding.UTF8.GetBytes(AppConst.AesKey.Substring(0, 16));
			aesAlg.Padding = PaddingMode.PKCS7;
			aesAlg.Mode = CipherMode.CBC;

			ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
			using MemoryStream msEncrypt = new MemoryStream();
			using CryptoStream csEncrypt = new CryptoStream(msEncrypt, decryptor, CryptoStreamMode.Write);
			csEncrypt.Write(encryptedBuffer, 0, encryptedBuffer.Length);
			csEncrypt.FlushFinalBlock();
			return msEncrypt.ToArray();
		}
		/// <summary>
		/// 将指定的16进制字符串转换为byte数组
		/// </summary>
		/// <param name="s">16进制字符串(如：“7F 2C 4A”或“7F2C4A”都可以)</param>
		/// <returns>16进制字符串对应的byte数组</returns>
		public static byte[] HexStringToByteArray(string s)
		{
			s = s.Replace(" ", "");
			byte[] buffer = new byte[s.Length / 2];
			for (int i = 0; i < s.Length; i += 2)
				buffer[i / 2] = Convert.ToByte(s.Substring(i, 2), 16);
			return buffer;
		}

		/// <summary>
		/// 将一个byte数组转换成一个格式化的16进制字符串
		/// </summary>
		/// <param name="data">byte数组</param>
		/// <returns>格式化的16进制字符串</returns>
		public static string ByteArrayToHexString(byte[] data)
		{
			StringBuilder sb = new StringBuilder(data.Length * 3);
			foreach (byte b in data)
			{
				//16进制数字
				sb.Append(Convert.ToString(b, 16).PadLeft(2, '0'));
			}
			return sb.ToString().ToUpper();
		}
	}
}
