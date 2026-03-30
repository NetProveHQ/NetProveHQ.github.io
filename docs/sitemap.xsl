<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="2.0"
  xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
  xmlns:sitemap="http://www.sitemaps.org/schemas/sitemap/0.9">
<xsl:output method="html" encoding="UTF-8" indent="yes"/>
<xsl:template match="/">
<html>
<head>
  <title>NetProve - Site Haritası</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; margin: 0; padding: 40px; background: #0f172a; color: #f1f5f9; }
    h1 { color: #3b82f6; font-size: 24px; margin-bottom: 8px; }
    p { color: #94a3b8; margin-bottom: 24px; }
    table { width: 100%; border-collapse: collapse; background: #1e293b; border-radius: 8px; overflow: hidden; }
    th { background: #334155; padding: 12px 16px; text-align: left; font-size: 14px; color: #94a3b8; text-transform: uppercase; letter-spacing: 0.05em; }
    td { padding: 12px 16px; border-top: 1px solid #334155; font-size: 14px; }
    a { color: #3b82f6; text-decoration: none; }
    a:hover { text-decoration: underline; }
  </style>
</head>
<body>
  <h1>NetProve Site Haritası</h1>
  <p>Bu site haritası arama motorlarının siteyi taramasına yardımcı olur.</p>
  <table>
    <tr>
      <th>URL</th>
      <th>Son Güncelleme</th>
    </tr>
    <xsl:for-each select="sitemap:urlset/sitemap:url">
    <tr>
      <td><a href="{sitemap:loc}"><xsl:value-of select="sitemap:loc"/></a></td>
      <td><xsl:value-of select="sitemap:lastmod"/></td>
    </tr>
    </xsl:for-each>
  </table>
</body>
</html>
</xsl:template>
</xsl:stylesheet>
