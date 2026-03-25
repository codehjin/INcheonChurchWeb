using OtpNet;
using QRCoder;
using System.Web;

namespace INcheonChurchWeb.Services
{
    public class TwoFactorAuthService
    {
        // 1. 새로운 비밀키 생성 (사용자가 처음 설정할 때 사용)
        public string GenerateSecret()
        {
            var key = KeyGeneration.GenerateRandomKey(20);
            return Base32Encoding.ToString(key);
        }

        // 2. 구글 인증앱에 등록할 QR 코드 이미지(Base64) 생성
        public string GetQrCodeImage(string username, string secret)
        {
            string issuer = "IncheonChurch";
            string encodedIssuer = HttpUtility.UrlEncode(issuer);
            string url = $"otpauth://totp/{encodedIssuer}:{username}?secret={secret}&issuer={encodedIssuer}";

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeAsPngByteArr = qrCode.GetGraphic(20);

            return $"data:image/png;base64,{Convert.ToBase64String(qrCodeAsPngByteArr)}";
        }

        // 3. 사용자가 입력한 6자리 번호 검증
        public bool VerifyCode(string secret, string inputCode)
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(inputCode)) return false;

            var key = Base32Encoding.ToBytes(secret);
            var totp = new Totp(key);

            // 시간 오차를 고려하여 앞뒤 30초 정도의 유효 범위 내에서 검증
            return totp.VerifyTotp(inputCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);
        }
    }
}