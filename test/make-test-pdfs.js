const fs = require('fs');
const path = require('path');

function escapeText(s) {
  return s.replace(/\\/g, '\\\\').replace(/\(/g, '\\(').replace(/\)/g, '\\)');
}

function generate(pages, options = {}) {
  const objs = [];
  const pageObjStart = 4;
  const kids = pages.map((_, i) => `${pageObjStart + i * 2} 0 R`).join(' ');
  let catalogExtra = '';
  let trailerExtra = '';

  if (options.rich) {
    const first = pageObjStart + pages.length * 2;
    const infoRef = first;
    const outlineRef = first + 1;
    const firstItemRef = first + 2;
    const secondItemRef = first + 3;
    const fileSpecRef = first + 4;
    const embeddedRef = first + 5;

    objs[infoRef] = `<< /Title (Documento enriquecido) /Author (MSoft) ` +
      `/Subject (Pruebas de Mpdf) /Keywords (mpdf pdfium pruebas) ` +
      `/Creator (make-test-pdfs.js) /Producer (Mpdf tests) ` +
      `/CreationDate (D:20261107120000Z) /ModDate (D:20261107121500Z) >>`;
    objs[outlineRef] = `<< /Type /Outlines /First ${firstItemRef} 0 R ` +
      `/Last ${secondItemRef} 0 R /Count 2 >>`;
    objs[firstItemRef] = `<< /Title (Inicio del documento) /Parent ${outlineRef} 0 R ` +
      `/Dest [${pageObjStart} 0 R /Fit] /Next ${secondItemRef} 0 R >>`;
    objs[secondItemRef] = `<< /Title (Pagina de busqueda) /Parent ${outlineRef} 0 R ` +
      `/Dest [${pageObjStart + 2} 0 R /Fit] /Prev ${firstItemRef} 0 R >>`;
    objs[fileSpecRef] = `<< /Type /Filespec /F (nota.txt) /UF (nota.txt) ` +
      `/EF << /F ${embeddedRef} 0 R >> >>`;
    const attachment = 'Adjunto de prueba de Mpdf.\n';
    objs[embeddedRef] = `<< /Type /EmbeddedFile /Subtype /text#2Fplain ` +
      `/Length ${Buffer.byteLength(attachment, 'latin1')} >>\nstream\n${attachment}\nendstream`;

    catalogExtra = ` /Outlines ${outlineRef} 0 R ` +
      `/PageLabels << /Nums [0 << /S /D /St 1 >> 2 << /S /r >>] >> ` +
      `/Names << /EmbeddedFiles << /Names [(nota.txt) ${fileSpecRef} 0 R] >> >>`;
    trailerExtra = ` /Info ${infoRef} 0 R`;
  }

  objs[1] = `<< /Type /Catalog /Pages 2 0 R${catalogExtra} >>`;
  objs[2] = `<< /Type /Pages /Kids [${kids}] /Count ${pages.length} >>`;
  objs[3] = `<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>`;

  pages.forEach((page, i) => {
    const width = page.width || 595;
    const height = page.height || 842;
    const contentIndex = pageObjStart + i * 2 + 1;
    const rotate = page.rotate ? ` /Rotate ${page.rotate}` : '';
    objs[pageObjStart + i * 2] =
      `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${width} ${height}] ` +
      `/Resources << /Font << /F1 3 0 R >> >> /Contents ${contentIndex} 0 R${rotate} >>`;

    let stream = `BT /F1 26 Tf 72 770 Td (${escapeText(page.title)}) Tj ET\n`;
    (page.lines || []).forEach((line, li) => {
      stream += `BT /F1 13 Tf 72 ${720 - li * 26} Td (${escapeText(line)}) Tj ET\n`;
    });
    if (page.box) {
      stream += `1 0 0 RG 3 w 72 ${page.boxY || 560} 320 46 re S\n`;
      stream += `0.85 g 72 120 451 60 re f\n0 g\n`;
      stream += `BT /F1 11 Tf 84 145 Td (${escapeText('Zona inferior de la pagina ' + (i + 1))}) Tj ET\n`;
    }
    objs[contentIndex] = `<< /Length ${Buffer.byteLength(stream, 'latin1')} >>\nstream\n${stream}\nendstream`;
  });

  let out = '%PDF-1.7\n%\xE2\xE3\xCF\xD3\n';
  const offsets = [];
  for (let i = 1; i < objs.length; i++) {
    offsets[i] = Buffer.byteLength(out, 'latin1');
    out += `${i} 0 obj\n${objs[i]}\nendobj\n`;
  }
  const xref = Buffer.byteLength(out, 'latin1');
  out += `xref\n0 ${objs.length}\n0000000000 65535 f \n`;
  for (let i = 1; i < objs.length; i++) out += `${String(offsets[i]).padStart(10, '0')} 00000 n \n`;
  out += `trailer\n<< /Size ${objs.length} /Root 1 0 R${trailerExtra} >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(out, 'latin1');
}

const dir = __dirname;

const sample = [
  {
    title: 'Mpdf - Documento de prueba',
    box: true,
    lines: [
      'Editor PDF gratuito y de codigo abierto.',
      'Motor de renderizado: PDFium.',
      'Esta pagina sirve para comprobar el zoom y el desplazamiento.',
      'Arrastra sobre el texto para resaltar, subrayar o tachar.',
      'Usa el lapiz para dibujar y el borrador para quitar marcas.'
    ]
  },
  {
    title: 'Busqueda y anotaciones',
    box: true,
    lines: [
      'Mpdf puede buscar texto dentro del documento.',
      'Prueba a buscar la palabra Mpdf: aparece varias veces.',
      'Mpdf resalta las coincidencias y navega con F3.'
    ]
  },
  {
    title: 'Pagina girada 90 grados',
    box: true,
    rotate: 90,
    lines: [
      'Esta pagina tiene una rotacion de 90 grados.',
      'Al girarla de nuevo, las marcas deben seguir al contenido.',
      'Mpdf conserva las paginas y sus anotaciones.'
    ]
  },
  {
    title: 'Pagina de formato pequeno',
    width: 420,
    height: 595,
    box: true,
    boxY: 420,
    lines: [
      'Una pagina con un tamano distinto.',
      'El modo ajustar al ancho debe funcionar.',
      'Ultima pagina del documento de prueba.'
    ]
  }
];

fs.writeFileSync(path.join(dir, 'sample.pdf'), generate(sample));
fs.writeFileSync(path.join(dir, 'merge-a.pdf'), generate([
  { title: 'Documento A', box: true, lines: ['Primer PDF para combinar.', 'Pagina uno de dos.'] },
  { title: 'Documento A - pagina 2', box: false, lines: ['Segunda pagina del documento A.'] }
]));
fs.writeFileSync(path.join(dir, 'merge-b.pdf'), generate([
  { title: 'Documento B', box: true, lines: ['Segundo PDF para combinar.', 'Pagina unica.'] }
]));
fs.writeFileSync(path.join(dir, 'rich.pdf'), generate([
  {
    title: 'Documento enriquecido',
    box: true,
    lines: ['Metadatos, marcadores y adjuntos.', 'Busqueda de texto en Mpdf.']
  },
  {
    title: 'Pagina de busqueda',
    box: false,
    lines: ['Mpdf busca en todo el documento.', 'PDFium extrae el texto de cada pagina.']
  },
  {
    title: 'Pagina con etiqueta romana',
    box: false,
    lines: ['Etiquetas de pagina personalizadas.']
  }
], { rich: true }));

console.log('PDFs de prueba generados en ' + dir);
