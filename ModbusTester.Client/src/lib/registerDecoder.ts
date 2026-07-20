import type { ModbusDataSnapshot } from "../types/modbus";
import { toBinaryString16 } from "./binary";

export interface DecodedRow {
  /** Raw protocol address — used for selection/writes, since the backend expects raw addresses. */
  address: number;
  /** Modicon-style address (e.g. 40000 + address for holding registers) shown to the user. */
  displayAddress: number;
  text: string;
  /**
   * True for a row added purely to round the visible table out to a full decade (see
   * buildAlignedRows) — no data exists at this address in the current read, so the cell is
   * blank and every interaction (select/edit/write) is disabled for it.
   */
  isPlaceholder: boolean;
}

function combineBigEndian(words: number[]): DataView {
  const view = new DataView(new ArrayBuffer(words.length * 2));
  words.forEach((word, i) => view.setUint16(i * 2, word & 0xffff, false));
  return view;
}

function decodeItem(dataType: ModbusDataSnapshot["dataType"], words: number[]): string {
  switch (dataType) {
    case "Signed (16-bit)":
      return combineBigEndian(words).getInt16(0, false).toString();
    case "Binary (String)":
      return toBinaryString16(words[0]);
    case "Long (32-bit)":
      return combineBigEndian(words).getInt32(0, false).toString();
    case "Long Inverse (32-bit)":
      return combineBigEndian([words[1], words[0]]).getInt32(0, false).toString();
    case "Float (32-bit)":
      return combineBigEndian(words).getFloat32(0, false).toPrecision(7);
    case "Float Inverse (32-bit)":
      return combineBigEndian([words[1], words[0]]).getFloat32(0, false).toPrecision(7);
    case "Double (64-bit)":
      return combineBigEndian(words).getFloat64(0, false).toPrecision(15);
    case "Double Inverse (64-bit)":
      return combineBigEndian([words[3], words[2], words[1], words[0]]).getFloat64(0, false).toPrecision(15);
    case "Unsigned (16-bit)":
    default:
      return words[0].toString();
  }
}

/**
 * Pads [startAddress, startAddress + spanCount - 1] out to the enclosing multiple-of-ten address
 * range — e.g. a read starting at 16 for 10 registers (real range 16-25) renders as 10-29 — so
 * every column ends up exactly ROWS_PER_COLUMN rows and its address column reads as a clean decade
 * (40010-40019, 40020-40029) instead of starting/ending mid-decade. Addresses outside the real
 * read range come back as blank, disabled placeholder rows; the backend read itself is unaffected,
 * it still only ever requests [startAddress, startAddress + spanCount - 1].
 */
function buildAlignedRows(
  startAddress: number,
  spanCount: number,
  addressBase: number,
  valueForAddress: (address: number) => string,
): DecodedRow[] {
  if (spanCount <= 0) return [];

  const alignedStart = Math.floor(startAddress / 10) * 10;
  const alignedEnd = Math.ceil((startAddress + spanCount) / 10) * 10 - 1;
  const realEnd = startAddress + spanCount - 1;

  const rows: DecodedRow[] = [];
  for (let addr = alignedStart; addr <= alignedEnd; addr++) {
    const isPlaceholder = addr < startAddress || addr > realEnd;
    rows.push({
      address: addr,
      displayAddress: addr + addressBase,
      text: isPlaceholder ? "" : valueForAddress(addr),
      isPlaceholder,
    });
  }
  return rows;
}

/** Splits a flat register read into per-item rows, decoding each per the snapshot's DataType. */
export function decodeRegisters(snapshot: ModbusDataSnapshot, addressBase: number): DecodedRow[] {
  const { registers, startAddress, registerSizePerItem, dataType } = snapshot;
  if (!registers || registers.length === 0) return [];

  // Decade-alignment padding only makes sense one register per row — a padded row for a
  // multi-register type (Long/Float/Double) could split a real value's registers across the
  // real/placeholder boundary, so those data types keep the original unpadded, one-row-per-item
  // layout instead.
  if (registerSizePerItem !== 1) {
    const rows: DecodedRow[] = [];
    for (let i = 0; i + registerSizePerItem <= registers.length; i += registerSizePerItem) {
      rows.push({
        address: startAddress + i,
        displayAddress: startAddress + i + addressBase,
        text: decodeItem(dataType, registers.slice(i, i + registerSizePerItem)),
        isPlaceholder: false,
      });
    }
    return rows;
  }

  return buildAlignedRows(startAddress, registers.length, addressBase, (addr) =>
    decodeItem(dataType, [registers[addr - startAddress]]),
  );
}

export function decodeBits(snapshot: ModbusDataSnapshot, addressBase: number): DecodedRow[] {
  const { bits, startAddress } = snapshot;
  if (!bits || bits.length === 0) return [];

  return buildAlignedRows(startAddress, bits.length, addressBase, (addr) => (bits[addr - startAddress] ? "1" : "0"));
}
