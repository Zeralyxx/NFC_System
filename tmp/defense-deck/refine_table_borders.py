from pathlib import Path
import sys
import zipfile
from copy import deepcopy
from lxml import etree


path = Path(sys.argv[1]).resolve()
ns = {"a": "http://schemas.openxmlformats.org/drawingml/2006/main"}
uri = ns["a"]
with zipfile.ZipFile(path) as source:
    entries = [(item, source.read(item.filename)) for item in source.infolist()]

# Restore the exact chart workbooks omitted by the presentation import/export.
# Cached values and formulas must match before copying a source package part.
chart_ns = {"c": "http://schemas.openxmlformats.org/drawingml/2006/chart"}
with zipfile.ZipFile(Path(sys.argv[2]).resolve()) as original:
    original_parts = {name: original.read(name) for name in original.namelist()}
original_types = etree.fromstring(original_parts["[Content_Types].xml"])
existing_names = {item.filename for item, _ in entries}

revised = []
for item, data in entries:
    if "/charts/chart" in item.filename and item.filename.endswith(".xml"):
        reference = etree.fromstring(original_parts[item.filename])
        root = etree.fromstring(data)
        for query in (".//c:f", ".//c:numCache/c:pt/c:v", ".//c:strCache/c:pt/c:v"):
            expected = [node.text for node in reference.findall(query, chart_ns)]
            actual = [node.text for node in root.findall(query, chart_ns)]
            if expected != actual:
                raise ValueError(f"Source chart data changed: {item.filename}, {query}")
        if root.find("c:externalData", chart_ns) is None:
            root.append(deepcopy(reference.find("c:externalData", chart_ns)))
        if len(sys.argv) > 3 and sys.argv[3] == "horizontal":
            for axis in root.findall(".//c:catAx", chart_ns):
                text_properties = axis.find("c:txPr", chart_ns)
                if text_properties is not None:
                    body = text_properties.find("a:bodyPr", ns)
                    if body is None:
                        body = etree.Element(f"{{{uri}}}bodyPr")
                        text_properties.insert(0, body)
                    body.set("rot", "0")
                    body.set("vert", "horz")
                    body.set("wrap", "square")
                skip = axis.find("c:tickLblSkip", chart_ns)
                if skip is None:
                    skip = etree.Element(f"{{{chart_ns['c']}}}tickLblSkip", val="1")
                    later = next((node for node in axis if etree.QName(node).localname in ("tickMarkSkip", "noMultiLvlLbl", "extLst")), None)
                    if later is None:
                        axis.append(skip)
                    else:
                        axis.insert(axis.index(later), skip)
                skip.set("val", "1")
        data = etree.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)
    if item.filename == "[Content_Types].xml":
        root = etree.fromstring(data)
        for definition in original_types:
            if definition.get("Extension") == "xlsx" and not any(node.get("Extension") == "xlsx" for node in root):
                root.append(deepcopy(definition))
        data = etree.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)
    if item.filename.startswith("ppt/slides/slide") and item.filename.endswith(".xml"):
        root = etree.fromstring(data)
        changed = False
        for table in root.findall(".//a:tbl", ns):
            for row_index, row in enumerate(table.findall("a:tr", ns)):
                for cell in row.findall("a:tc", ns):
                    properties = cell.find("a:tcPr", ns)
                    if properties is None:
                        properties = etree.SubElement(cell, f"{{{uri}}}tcPr")
                    for edge_index, edge in enumerate(("lnL", "lnR", "lnT", "lnB")):
                        for old in properties.findall(f"a:{edge}", ns):
                            properties.remove(old)
                        line = etree.Element(f"{{{uri}}}{edge}", w="6350")
                        if edge == "lnB" and row_index > 0:
                            fill = etree.SubElement(line, f"{{{uri}}}solidFill")
                            etree.SubElement(fill, f"{{{uri}}}srgbClr", val="DCE5E9")
                        else:
                            etree.SubElement(line, f"{{{uri}}}noFill")
                        etree.SubElement(line, f"{{{uri}}}prstDash", val="solid")
                        properties.insert(edge_index, line)
                    changed = True
        if changed:
            data = etree.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)
    revised.append((item, data))

for name, data in original_parts.items():
    if ("/charts/_rels/" in name or name.startswith("ppt/embeddings/")) and name not in existing_names:
        revised.append((name, data))

temporary = path.with_name(path.stem + "-borders.pptx")
with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED) as target:
    for item, data in revised:
        target.writestr(item, data)
temporary.replace(path)
print("Applied native row-divider styling and preserved source chart workbooks")
