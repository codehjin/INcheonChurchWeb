using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;

namespace INcheonChurchWeb.Services
{
    public class FileService
    {
        private readonly IWebHostEnvironment _env;

        public FileService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public string EnsureReceiptUploadFolder()
        {
            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "receipts");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            return uploadsFolder;
        }

        public string BuildReceiptRelativePath(string fileName)
        {
            return $"/uploads/receipts/{fileName}";
        }

        public string BuildReceiptFileName(int departmentId, string extension = ".jpg", bool includeMilliseconds = false)
        {
            var timestamp = includeMilliseconds
                ? DateTime.Now.ToString("yyyyMMddHHmmssfff")
                : DateTime.Now.ToString("yyyyMMddHHmmss");

            return $"receipt_{departmentId}_{timestamp}{extension}";
        }

        public async Task SaveBrowserFileAsync(IBrowserFile file, string fileName, long maxAllowedSize)
        {
            var uploadsFolder = EnsureReceiptUploadFolder();
            var filePath = Path.Combine(uploadsFolder, fileName);

            using var fileStream = new FileStream(filePath, FileMode.Create);
            await file.OpenReadStream(maxAllowedSize: maxAllowedSize).CopyToAsync(fileStream);
        }

        public Task SaveBytesAsync(byte[] bytes, string fileName)
        {
            var uploadsFolder = EnsureReceiptUploadFolder();
            var filePath = Path.Combine(uploadsFolder, fileName);

            return File.WriteAllBytesAsync(filePath, bytes);
        }

        public string GetPhysicalPathFromRelative(string relativePath)
        {
            return Path.Combine(_env.WebRootPath, relativePath.TrimStart('/'));
        }

        public void DeleteByRelativePath(string relativePath)
        {
            var filePath = GetPhysicalPathFromRelative(relativePath);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        public bool TryMoveByRelativePath(string oldRelativePath, string newFileName, out string newRelativePath)
        {
            var oldPhysicalPath = GetPhysicalPathFromRelative(oldRelativePath);
            if (!File.Exists(oldPhysicalPath))
            {
                newRelativePath = oldRelativePath;
                return false;
            }

            EnsureReceiptUploadFolder();
            var newPhysicalPath = Path.Combine(_env.WebRootPath, "uploads", "receipts", newFileName);
            newRelativePath = BuildReceiptRelativePath(newFileName);

            File.Move(oldPhysicalPath, newPhysicalPath);
            return true;
        }
    }
}
