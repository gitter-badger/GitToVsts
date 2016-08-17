﻿using System;
using System.IO;
using System.Net;
using System.Windows.Media.Imaging;
using GitToVsts.Internal.Models;

namespace GitToVsts.Internal.Git
{
    public class ConvertGitAvatart : IGitAvatar
    {
        public BitmapImage For(GitUser gitUser)
        {
            var image = new BitmapImage();
            const int bytesToRead = 100;

            if (string.IsNullOrWhiteSpace(gitUser.Avatar_Url))
            {
                return image;
            }
            var pictureUri = new Uri(gitUser.Avatar_Url, UriKind.Absolute);
            var request = WebRequest.Create(pictureUri);
            request.Timeout = -1;
            var response = request.GetResponse();
            var responseStream = response.GetResponseStream();
            if (responseStream != null)
            {
                var reader = new BinaryReader(responseStream);
                var memoryStream = new MemoryStream();

                var bytebuffer = new byte[bytesToRead];
                var bytesRead = reader.Read(bytebuffer, 0, bytesToRead);

                while (bytesRead > 0)
                {
                    memoryStream.Write(bytebuffer, 0, bytesRead);
                    bytesRead = reader.Read(bytebuffer, 0, bytesToRead);
                }

                image.BeginInit();
                memoryStream.Seek(0, SeekOrigin.Begin);

                image.StreamSource = memoryStream;
            }
            image.EndInit();
            return image;
        }
    }
}