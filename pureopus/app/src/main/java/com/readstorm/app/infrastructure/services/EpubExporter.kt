package com.readstorm.app.infrastructure.services

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileOutputStream
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

/**
 * Lightweight EPUB 3 generator — no external dependencies beyond java.util.zip.
 * Produces a valid EPUB file from title, author, and chapter list.
 */
object EpubExporter {

    suspend fun export(
        workDirectory: String,
        title: String,
        author: String,
        sourceId: Int,
        chapters: List<Pair<String, String>> // (chapterTitle, chapterContent)
    ): String = withContext(Dispatchers.IO) {
        val downloadDir = File(workDirectory, "downloads").also { it.mkdirs() }
        val safeName = "${title}(${author}).epub"
            .replace(Regex("[/\\\\:*?\"<>|]"), "_")
        val outputFile = File(downloadDir, safeName)

        ZipOutputStream(FileOutputStream(outputFile)).use { zip ->
            // 1. mimetype (must be first, uncompressed)
            val mimeEntry = ZipEntry("mimetype")
            mimeEntry.method = ZipEntry.STORED
            val mimeBytes = "application/epub+zip".toByteArray()
            mimeEntry.size = mimeBytes.size.toLong()
            mimeEntry.compressedSize = mimeBytes.size.toLong()
            val crc = java.util.zip.CRC32()
            crc.update(mimeBytes)
            mimeEntry.crc = crc.value
            zip.putNextEntry(mimeEntry)
            zip.write(mimeBytes)
            zip.closeEntry()

            // 2. META-INF/container.xml
            zip.putNextEntry(ZipEntry("META-INF/container.xml"))
            zip.write(containerXml().toByteArray())
            zip.closeEntry()

            // 3. OEBPS/content.opf
            zip.putNextEntry(ZipEntry("OEBPS/content.opf"))
            zip.write(contentOpf(title, author, chapters.size).toByteArray())
            zip.closeEntry()

            // 4. OEBPS/toc.ncx
            zip.putNextEntry(ZipEntry("OEBPS/toc.ncx"))
            zip.write(tocNcx(title, chapters).toByteArray())
            zip.closeEntry()

            // 5. OEBPS/toc.xhtml
            zip.putNextEntry(ZipEntry("OEBPS/toc.xhtml"))
            zip.write(tocXhtml(title, chapters).toByteArray())
            zip.closeEntry()

            // 6. Chapter files
            chapters.forEachIndexed { index, (chTitle, content) ->
                zip.putNextEntry(ZipEntry("OEBPS/chapter-${index + 1}.xhtml"))
                zip.write(chapterXhtml(chTitle, content).toByteArray())
                zip.closeEntry()
            }
        }

        outputFile.absolutePath
    }

    private fun escapeXml(text: String): String =
        text.replace("&", "&amp;")
            .replace("<", "&lt;")
            .replace(">", "&gt;")
            .replace("\"", "&quot;")
            .replace("'", "&apos;")

    private fun containerXml(): String = """<?xml version="1.0" encoding="UTF-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles>
    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
  </rootfiles>
</container>"""

    private fun contentOpf(title: String, author: String, chapterCount: Int): String {
        val items = (1..chapterCount).joinToString("\n    ") {
            """<item id="chapter-$it" href="chapter-$it.xhtml" media-type="application/xhtml+xml"/>"""
        }
        val spine = (1..chapterCount).joinToString("\n    ") {
            """<itemref idref="chapter-$it"/>"""
        }
        return """<?xml version="1.0" encoding="UTF-8"?>
<package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="3.0">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="BookId">urn:uuid:readstorm-${System.currentTimeMillis()}</dc:identifier>
    <dc:title>${escapeXml(title)}</dc:title>
    <dc:creator>${escapeXml(author)}</dc:creator>
    <dc:language>zh</dc:language>
    <meta property="dcterms:modified">${java.time.Instant.now()}</meta>
  </metadata>
  <manifest>
    <item id="toc" href="toc.xhtml" media-type="application/xhtml+xml" properties="nav"/>
    <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
    $items
  </manifest>
  <spine toc="ncx">
    $spine
  </spine>
</package>"""
    }

    private fun tocNcx(title: String, chapters: List<Pair<String, String>>): String {
        val navPoints = chapters.mapIndexed { index, (chTitle, _) ->
            """    <navPoint id="navpoint-${index + 1}" playOrder="${index + 1}">
      <navLabel><text>${escapeXml(chTitle)}</text></navLabel>
      <content src="chapter-${index + 1}.xhtml"/>
    </navPoint>"""
        }.joinToString("\n")
        return """<?xml version="1.0" encoding="UTF-8"?>
<ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
  <head><meta name="dtb:uid" content="urn:uuid:readstorm"/></head>
  <docTitle><text>${escapeXml(title)}</text></docTitle>
  <navMap>
$navPoints
  </navMap>
</ncx>"""
    }

    private fun tocXhtml(title: String, chapters: List<Pair<String, String>>): String {
        val items = chapters.mapIndexed { index, (chTitle, _) ->
            """      <li><a href="chapter-${index + 1}.xhtml">${escapeXml(chTitle)}</a></li>"""
        }.joinToString("\n")
        return """<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
<head><title>${escapeXml(title)}</title></head>
<body>
  <nav epub:type="toc">
    <h1>目录</h1>
    <ol>
$items
    </ol>
  </nav>
</body>
</html>"""
    }

    private fun chapterXhtml(title: String, content: String): String {
        val paragraphs = content.split("\n")
            .filter { it.isNotBlank() }
            .joinToString("\n") { "  <p>${escapeXml(it)}</p>" }
        return """<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head><title>${escapeXml(title)}</title></head>
<body>
  <h2>${escapeXml(title)}</h2>
$paragraphs
</body>
</html>"""
    }
}
