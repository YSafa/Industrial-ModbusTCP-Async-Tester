import type { ModbusDataSnapshot } from "../types/modbus";
import { toBinaryString16 } from "./binary";

export interface DecodedRow {
  /** Raw protocol address — used for selection/writes, since the backend expects raw addresses. */
  address: number;
  /** Modicon-style address (e.g. 40000 + address for holding registers) shown to the user. */
  displayAddress: number;
  text: string;
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

/** Splits a flat register read into per-item rows, decoding each per the snapshot's DataType. */
export function decodeRegisters(snapshot: ModbusDataSnapshot, addressBase: number): DecodedRow[] {
  const { registers, startAddress, registerSizePerItem, dataType } = snapshot;
  if (!registers || registers.length === 0) return [];

  const rows: DecodedRow[] = [];
  for (let i = 0; i + registerSizePerItem <= registers.length; i += registerSizePerItem) {
    rows.push({
      address: startAddress + i,
      displayAddress: startAddress + i + addressBase,
      text: decodeItem(dataType, registers.slice(i, i + registerSizePerItem)),
    });
  }
  return rows;
}

export function decodeBits(snapshot: ModbusDataSnapshot, addressBase: number): DecodedRow[] {
  const { bits, startAddress } = snapshot;
  if (!bits) return [];
  return bits.map((bit, i) => ({
    address: startAddress + i,
    displayAddress: startAddress + i + addressBase,
    text: bit ? "1" : "0",
  }));
}
