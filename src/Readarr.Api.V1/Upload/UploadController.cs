using System.IO;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.RootFolders;
using Readarr.Http;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Upload
{
    [V1ApiController]
    public class UploadController : Controller
    {
        private readonly IRootFolderService _rootFolderService;
        private readonly IDiskProvider _diskProvider;

        public UploadController(IRootFolderService rootFolderService, IDiskProvider diskProvider)
        {
            _rootFolderService = rootFolderService;
            _diskProvider = diskProvider;
        }

        [HttpPost]
        [RequestFormLimits(MultipartBodyLengthLimit = 2000000000)]
        [RequestSizeLimit(2000000000)]
        public IActionResult UploadFile([FromQuery] string folder = null)
        {
            var files = Request.Form.Files;

            if (files.Empty())
            {
                throw new BadRequestException("No file provided");
            }

            // Determine target folder
            string targetFolder;
            if (!string.IsNullOrWhiteSpace(folder))
            {
                targetFolder = folder;
            }
            else
            {
                var rootFolders = _rootFolderService.All();
                if (rootFolders.Count == 0)
                {
                    throw new BadRequestException("No root folder configured and no folder specified");
                }

                targetFolder = Path.Combine(rootFolders[0].Path, "Uploads");
            }

            if (!_diskProvider.FolderExists(targetFolder))
            {
                _diskProvider.CreateFolder(targetFolder);
            }

            var uploadedFiles = new System.Collections.Generic.List<object>();

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file.FileName);
                var destPath = Path.Combine(targetFolder, fileName);

                _diskProvider.SaveStream(file.OpenReadStream(), destPath);

                uploadedFiles.Add(new
                {
                    name = fileName,
                    path = destPath,
                    size = file.Length
                });
            }

            return Ok(new { folder = targetFolder, files = uploadedFiles });
        }
    }
}
