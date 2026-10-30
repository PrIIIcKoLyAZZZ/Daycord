import AppKit
import Foundation

guard CommandLine.arguments.count == 3 else {
    fputs("Usage: create-macos-dmg-background.swift <output.png> <version>\n", stderr)
    exit(2)
}

let outputPath = CommandLine.arguments[1]
let version = CommandLine.arguments[2]

let width: CGFloat = 680
let height: CGFloat = 420

func color(_ hex: UInt32) -> NSColor {
    NSColor(
        calibratedRed: CGFloat((hex >> 16) & 255) / 255,
        green: CGFloat((hex >> 8) & 255) / 255,
        blue: CGFloat(hex & 255) / 255,
        alpha: 1
    )
}

let graphite = color(0x222428)
let amber = color(0xF0AB46)
let white = color(0xF6F5F2)
let muted = color(0xADB0B6)
let cardFill = color(0xF2F0EB)
let cardBorder = color(0xD4D0C9)
let cardText = color(0x767A82)

func drawText(
    _ value: String,
    in rect: NSRect,
    size: CGFloat,
    weight: NSFont.Weight,
    color: NSColor,
    alignment: NSTextAlignment = .left
) {
    let paragraph = NSMutableParagraphStyle()
    paragraph.alignment = alignment

    let attributes: [NSAttributedString.Key: Any] = [
        .font: NSFont.systemFont(ofSize: size, weight: weight),
        .foregroundColor: color,
        .paragraphStyle: paragraph
    ]

    NSAttributedString(string: value, attributes: attributes).draw(in: rect)
}

func roundedRect(
    _ rect: NSRect,
    radius: CGFloat,
    fill: NSColor,
    stroke: NSColor? = nil
) {
    let path = NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
    fill.setFill()
    path.fill()

    if let stroke {
        stroke.setStroke()
        path.lineWidth = 1
        path.stroke()
    }
}

let image = NSImage(size: NSSize(width: width, height: height))
image.lockFocus()

graphite.setFill()
NSRect(x: 0, y: 0, width: width, height: height).fill()

amber.setFill()
NSRect(x: 0, y: height - 5, width: width, height: 5).fill()

drawText(
    "Daycord",
    in: NSRect(x: 54, y: 345, width: 320, height: 45),
    size: 32,
    weight: .semibold,
    color: white
)

drawText(
    "Drag Daycord into Applications to install",
    in: NSRect(x: 56, y: 314, width: 490, height: 24),
    size: 15,
    weight: .regular,
    color: muted
)

drawText(
    "v\(version)",
    in: NSRect(x: 530, y: 360, width: 95, height: 22),
    size: 13,
    weight: .medium,
    color: amber,
    alignment: .right
)

color(0x45474B).setFill()
NSRect(x: 54, y: 300, width: 572, height: 1).fill()

// Cards
roundedRect(
    NSRect(x: 66, y: 54, width: 212, height: 226),
    radius: 18,
    fill: cardFill,
    stroke: cardBorder
)

roundedRect(
    NSRect(x: 402, y: 54, width: 212, height: 226),
    radius: 18,
    fill: cardFill,
    stroke: cardBorder
)

// Section captions — moved slightly lower for better visual balance.
drawText(
    "01  APPLICATION",
    in: NSRect(x: 76, y: 240, width: 192, height: 18),
    size: 11,
    weight: .semibold,
    color: cardText,
    alignment: .center
)

drawText(
    "02  DESTINATION",
    in: NSRect(x: 412, y: 240, width: 192, height: 18),
    size: 11,
    weight: .semibold,
    color: cardText,
    alignment: .center
)

// Arrow — slightly higher and cleaner.
let arrowLine = NSBezierPath()
arrowLine.move(to: NSPoint(x: 312, y: 168))
arrowLine.line(to: NSPoint(x: 368, y: 168))
arrowLine.lineWidth = 4
arrowLine.lineCapStyle = .round
amber.setStroke()
arrowLine.stroke()

let arrowHead = NSBezierPath()
arrowHead.move(to: NSPoint(x: 353, y: 182))
arrowHead.line(to: NSPoint(x: 369, y: 168))
arrowHead.line(to: NSPoint(x: 353, y: 154))
arrowHead.lineWidth = 4
arrowHead.lineCapStyle = .round
arrowHead.lineJoinStyle = .round
arrowHead.stroke()

drawText(
    "DRAG TO INSTALL",
    in: NSRect(x: 282, y: 132, width: 116, height: 18),
    size: 10,
    weight: .medium,
    color: muted,
    alignment: .center
)

drawText(
    "Your time. Your day. Your rhythm.",
    in: NSRect(x: 160, y: 17, width: 360, height: 20),
    size: 12,
    weight: .regular,
    color: muted,
    alignment: .center
)

image.unlockFocus()

guard
    let tiff = image.tiffRepresentation,
    let bitmap = NSBitmapImageRep(data: tiff),
    let png = bitmap.representation(using: .png, properties: [:])
else {
    fputs("ERROR: Cannot encode background PNG.\n", stderr)
    exit(1)
}

let destination = URL(fileURLWithPath: outputPath)

do {
    try FileManager.default.createDirectory(
        at: destination.deletingLastPathComponent(),
        withIntermediateDirectories: true
    )
    try png.write(to: destination)
} catch {
    fputs("ERROR: \(error)\n", stderr)
    exit(1)
}