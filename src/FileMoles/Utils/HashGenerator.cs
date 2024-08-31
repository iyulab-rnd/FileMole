using System.Security.Cryptography;

namespace FileMoles.Utils
{
    internal class HashGenerator
    {
        public async Task<string> GenerateHashAsync(string filePath)
        {
            // 재시도 횟수 설정
            const int maxRetries = 3;
            const int delayBetweenRetries = 100; // 밀리초 단위

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    // 파일을 공유모드로 열어 읽기 작업 시 다른 프로세스의 접근을 허용
                    using var md5 = MD5.Create();
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var hash = await md5.ComputeHashAsync(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
                catch (IOException) when (attempt < maxRetries - 1)
                {
                    // 지정된 시간만큼 대기 후 재시도
                    await Task.Delay(delayBetweenRetries);
                }
            }

            // 마지막 시도 후에도 실패 시 예외 발생
            throw new IOException($"파일에 접근할 수 없습니다: {filePath}");
        }
    }
}
