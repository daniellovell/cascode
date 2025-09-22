// Verilog for library sky130_scl_9T created by Liberate 20.1.0.867 on Mon Mar 18 08:34:50 PDT 2024 for SDF version 2.1

// type: ADDF 
`timescale 1ns/10ps
`celldefine
module ADDFX1 (CO, S, A, B, CI);
	output CO, S;
	input A, B, CI;

	// Function
	wire A__bar, B__bar, CI__bar;
	wire int_fwire_0, int_fwire_1, int_fwire_2;
	wire int_fwire_3, int_fwire_4, int_fwire_5;
	wire int_fwire_6;

	and (int_fwire_0, B, CI);
	and (int_fwire_1, A, CI);
	and (int_fwire_2, A, B);
	or (CO, int_fwire_2, int_fwire_1, int_fwire_0);
	not (B__bar, B);
	not (A__bar, A);
	and (int_fwire_3, A__bar, B__bar, CI);
	not (CI__bar, CI);
	and (int_fwire_4, A__bar, B, CI__bar);
	and (int_fwire_5, A, B__bar, CI__bar);
	and (int_fwire_6, A, B, CI);
	or (S, int_fwire_6, int_fwire_5, int_fwire_4, int_fwire_3);

	// Timing
	specify
		if ((B & ~CI))
			(A => CO) = 0;
		if ((~B & CI))
			(A => CO) = 0;
		ifnone (A => CO) = 0;
		if ((A & ~CI))
			(B => CO) = 0;
		if ((~A & CI))
			(B => CO) = 0;
		ifnone (B => CO) = 0;
		if ((A & ~B))
			(CI => CO) = 0;
		if ((~A & B))
			(CI => CO) = 0;
		ifnone (CI => CO) = 0;
		if ((B & CI))
			(A => S) = 0;
		if ((~B & ~CI))
			(A => S) = 0;
		ifnone (A => S) = 0;
		if ((B & ~CI))
			(A => S) = 0;
		if ((~B & CI))
			(A => S) = 0;
		if ((A & CI))
			(B => S) = 0;
		if ((~A & ~CI))
			(B => S) = 0;
		ifnone (B => S) = 0;
		if ((A & ~CI))
			(B => S) = 0;
		if ((~A & CI))
			(B => S) = 0;
		if ((A & B))
			(CI => S) = 0;
		if ((~A & ~B))
			(CI => S) = 0;
		ifnone (CI => S) = 0;
		if ((A & ~B))
			(CI => S) = 0;
		if ((~A & B))
			(CI => S) = 0;
	endspecify
endmodule
`endcelldefine

// type: ADDH 
`timescale 1ns/10ps
`celldefine
module ADDHX1 (CO, S, A, B);
	output CO, S;
	input A, B;

	// Function
	wire A__bar, B__bar, int_fwire_0;
	wire int_fwire_1;

	and (CO, A, B);
	not (A__bar, A);
	and (int_fwire_0, A__bar, B);
	not (B__bar, B);
	and (int_fwire_1, A, B__bar);
	or (S, int_fwire_1, int_fwire_0);

	// Timing
	specify
		(A => CO) = 0;
		(B => CO) = 0;
		if (~B)
			(A => S) = 0;
		ifnone (A => S) = 0;
		if (B)
			(A => S) = 0;
		if (~A)
			(B => S) = 0;
		ifnone (B => S) = 0;
		if (A)
			(B => S) = 0;
	endspecify
endmodule
`endcelldefine

// type: AND2 
`timescale 1ns/10ps
`celldefine
module AND2X1 (Y, A, B);
	output Y;
	input A, B;

	// Function
	and (Y, A, B);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AND2 
`timescale 1ns/10ps
`celldefine
module AND2X2 (Y, A, B);
	output Y;
	input A, B;

	// Function
	and (Y, A, B);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AND3 
`timescale 1ns/10ps
`celldefine
module AND3X1 (Y, A, B, C);
	output Y;
	input A, B, C;

	// Function
	and (Y, A, B, C);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AND3 
`timescale 1ns/10ps
`celldefine
module AND3X2 (Y, A, B, C);
	output Y;
	input A, B, C;

	// Function
	and (Y, A, B, C);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AND4 
`timescale 1ns/10ps
`celldefine
module AND4X1 (Y, A, B, C, D);
	output Y;
	input A, B, C, D;

	// Function
	and (Y, A, B, C, D);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AND4 
`timescale 1ns/10ps
`celldefine
module AND4X2 (Y, A, B, C, D);
	output Y;
	input A, B, C, D;

	// Function
	and (Y, A, B, C, D);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AOI211 
`timescale 1ns/10ps
`celldefine
module AOI211X1 (Y, A0, A1, B0, C0);
	output Y;
	input A0, A1, B0, C0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire C0__bar, int_fwire_0, int_fwire_1;

	not (C0__bar, C0);
	not (B0__bar, B0);
	not (A1__bar, A1);
	and (int_fwire_0, A1__bar, B0__bar, C0__bar);
	not (A0__bar, A0);
	and (int_fwire_1, A0__bar, B0__bar, C0__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		(A0 => Y) = 0;
		(A1 => Y) = 0;
		if ((A0 & ~A1 & ~C0))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~C0))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & ~C0))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & ~A1 & ~B0))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0))
			(C0 => Y) = 0;
		if ((~A0 & ~A1 & ~B0))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AOI211 
`timescale 1ns/10ps
`celldefine
module AOI211X2 (Y, A0, A1, B0, C0);
	output Y;
	input A0, A1, B0, C0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire C0__bar, int_fwire_0, int_fwire_1;

	not (C0__bar, C0);
	not (B0__bar, B0);
	not (A1__bar, A1);
	and (int_fwire_0, A1__bar, B0__bar, C0__bar);
	not (A0__bar, A0);
	and (int_fwire_1, A0__bar, B0__bar, C0__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		(A0 => Y) = 0;
		(A1 => Y) = 0;
		if ((A0 & ~A1 & ~C0))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~C0))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & ~C0))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & ~A1 & ~B0))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0))
			(C0 => Y) = 0;
		if ((~A0 & ~A1 & ~B0))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AOI21 
`timescale 1ns/10ps
`celldefine
module AOI21X1 (Y, A0, A1, B0);
	output Y;
	input A0, A1, B0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire int_fwire_0, int_fwire_1;

	not (B0__bar, B0);
	not (A1__bar, A1);
	and (int_fwire_0, A1__bar, B0__bar);
	not (A0__bar, A0);
	and (int_fwire_1, A0__bar, B0__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		(A0 => Y) = 0;
		(A1 => Y) = 0;
		if ((A0 & ~A1))
			(B0 => Y) = 0;
		if ((~A0 & A1))
			(B0 => Y) = 0;
		if ((~A0 & ~A1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AOI21 
`timescale 1ns/10ps
`celldefine
module AOI21X2 (Y, A0, A1, B0);
	output Y;
	input A0, A1, B0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire int_fwire_0, int_fwire_1;

	not (B0__bar, B0);
	not (A1__bar, A1);
	and (int_fwire_0, A1__bar, B0__bar);
	not (A0__bar, A0);
	and (int_fwire_1, A0__bar, B0__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		(A0 => Y) = 0;
		(A1 => Y) = 0;
		if ((A0 & ~A1))
			(B0 => Y) = 0;
		if ((~A0 & A1))
			(B0 => Y) = 0;
		if ((~A0 & ~A1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AOI221 
`timescale 1ns/10ps
`celldefine
module AOI221X1 (Y, A0, A1, B0, B1, C0);
	output Y;
	input A0, A1, B0, B1, C0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, C0__bar, int_fwire_0;
	wire int_fwire_1, int_fwire_2, int_fwire_3;

	not (C0__bar, C0);
	not (B1__bar, B1);
	not (A1__bar, A1);
	and (int_fwire_0, A1__bar, B1__bar, C0__bar);
	not (B0__bar, B0);
	and (int_fwire_1, A1__bar, B0__bar, C0__bar);
	not (A0__bar, A0);
	and (int_fwire_2, A0__bar, B1__bar, C0__bar);
	and (int_fwire_3, A0__bar, B0__bar, C0__bar);
	or (Y, int_fwire_3, int_fwire_2, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if ((A1 & B0 & ~B1 & ~C0))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & B1 & ~C0))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & ~B1 & ~C0))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((A0 & B0 & ~B1 & ~C0))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & B1 & ~C0))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & ~B1 & ~C0))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & ~A1 & B1 & ~C0))
			(B0 => Y) = 0;
		if ((~A0 & A1 & B1 & ~C0))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & B1 & ~C0))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~C0))
			(B1 => Y) = 0;
		if ((~A0 & A1 & B0 & ~C0))
			(B1 => Y) = 0;
		if ((~A0 & ~A1 & B0 & ~C0))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & ~B1) | (~A0 & ~A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & ~B1))
			(C0 => Y) = 0;
		if ((~A0 & ~A1 & ~B0 & B1))
			(C0 => Y) = 0;
		if ((~A0 & ~A1 & ~B0 & ~B1))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AOI221 
`timescale 1ns/10ps
`celldefine
module AOI221X2 (Y, A0, A1, B0, B1, C0);
	output Y;
	input A0, A1, B0, B1, C0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, C0__bar, int_fwire_0;
	wire int_fwire_1, int_fwire_2, int_fwire_3;

	not (C0__bar, C0);
	not (B1__bar, B1);
	not (A1__bar, A1);
	and (int_fwire_0, A1__bar, B1__bar, C0__bar);
	not (B0__bar, B0);
	and (int_fwire_1, A1__bar, B0__bar, C0__bar);
	not (A0__bar, A0);
	and (int_fwire_2, A0__bar, B1__bar, C0__bar);
	and (int_fwire_3, A0__bar, B0__bar, C0__bar);
	or (Y, int_fwire_3, int_fwire_2, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if ((A1 & B0 & ~B1 & ~C0))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & B1 & ~C0))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & ~B1 & ~C0))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((A0 & B0 & ~B1 & ~C0))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & B1 & ~C0))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & ~B1 & ~C0))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & ~A1 & B1 & ~C0))
			(B0 => Y) = 0;
		if ((~A0 & A1 & B1 & ~C0))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & B1 & ~C0))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~C0))
			(B1 => Y) = 0;
		if ((~A0 & A1 & B0 & ~C0))
			(B1 => Y) = 0;
		if ((~A0 & ~A1 & B0 & ~C0))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & ~B1) | (~A0 & ~A1 & ~B0 & B1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & ~B1))
			(C0 => Y) = 0;
		if ((~A0 & ~A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((~A0 & ~A1 & ~B0 & ~B1))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AOI222 
`timescale 1ns/10ps
`celldefine
module AOI222X1 (Y, A0, A1, B0, B1, C0, C1);
	output Y;
	input A0, A1, B0, B1, C0, C1;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, C0__bar, C1__bar;
	wire int_fwire_0, int_fwire_1, int_fwire_2;
	wire int_fwire_3, int_fwire_4, int_fwire_5;
	wire int_fwire_6, int_fwire_7;

	not (C1__bar, C1);
	not (B1__bar, B1);
	not (A1__bar, A1);
	and (int_fwire_0, A1__bar, B1__bar, C1__bar);
	not (C0__bar, C0);
	and (int_fwire_1, A1__bar, B1__bar, C0__bar);
	not (B0__bar, B0);
	and (int_fwire_2, A1__bar, B0__bar, C1__bar);
	and (int_fwire_3, A1__bar, B0__bar, C0__bar);
	not (A0__bar, A0);
	and (int_fwire_4, A0__bar, B1__bar, C1__bar);
	and (int_fwire_5, A0__bar, B1__bar, C0__bar);
	and (int_fwire_6, A0__bar, B0__bar, C1__bar);
	and (int_fwire_7, A0__bar, B0__bar, C0__bar);
	or (Y, int_fwire_7, int_fwire_6, int_fwire_5, int_fwire_4, int_fwire_3, int_fwire_2, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if ((A1 & B0 & ~B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((A1 & B0 & ~B1 & ~C0 & C1))
			(A0 => Y) = 0;
		if ((A1 & B0 & ~B1 & ~C0 & ~C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & B1 & ~C0 & C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & B1 & ~C0 & ~C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & ~B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & ~B1 & ~C0 & C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & ~B1 & ~C0 & ~C1))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((A0 & B0 & ~B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((A0 & B0 & ~B1 & ~C0 & C1))
			(A1 => Y) = 0;
		if ((A0 & B0 & ~B1 & ~C0 & ~C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & B1 & ~C0 & C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & B1 & ~C0 & ~C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & ~B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & ~B1 & ~C0 & C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & ~B1 & ~C0 & ~C1))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & ~A1 & B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & B1 & ~C0 & C1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & B1 & ~C0 & ~C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & B1 & ~C0 & C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & B1 & ~C0 & ~C1))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & B1 & ~C0 & C1))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & B1 & ~C0 & ~C1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & ~A1 & B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~C0 & C1))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~C0 & ~C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & B0 & ~C0 & C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & B0 & ~C0 & ~C1))
			(B1 => Y) = 0;
		if ((~A0 & ~A1 & B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((~A0 & ~A1 & B0 & ~C0 & C1))
			(B1 => Y) = 0;
		if ((~A0 & ~A1 & B0 & ~C0 & ~C1))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1 & C1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1 & C1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & ~B1 & C1) | (~A0 & ~A1 & B0 & ~B1 & C1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1 & C1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1 & C1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & ~B1 & C1))
			(C0 => Y) = 0;
		if ((~A0 & ~A1 & ~B0 & B1 & C1))
			(C0 => Y) = 0;
		if ((~A0 & ~A1 & ~B0 & ~B1 & C1))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1 & C0))
			(C1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1 & C0))
			(C1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & ~B1 & C0) | (~A0 & ~A1 & B0 & ~B1 & C0))
			(C1 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1 & C0))
			(C1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1 & C0))
			(C1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & ~B1 & C0))
			(C1 => Y) = 0;
		if ((~A0 & ~A1 & ~B0 & B1 & C0))
			(C1 => Y) = 0;
		if ((~A0 & ~A1 & ~B0 & ~B1 & C0))
			(C1 => Y) = 0;
		ifnone (C1 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AOI222 
`timescale 1ns/10ps
`celldefine
module AOI222X2 (Y, A0, A1, B0, B1, C0, C1);
	output Y;
	input A0, A1, B0, B1, C0, C1;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, C0__bar, C1__bar;
	wire int_fwire_0, int_fwire_1, int_fwire_2;
	wire int_fwire_3, int_fwire_4, int_fwire_5;
	wire int_fwire_6, int_fwire_7;

	not (C1__bar, C1);
	not (B1__bar, B1);
	not (A1__bar, A1);
	and (int_fwire_0, A1__bar, B1__bar, C1__bar);
	not (C0__bar, C0);
	and (int_fwire_1, A1__bar, B1__bar, C0__bar);
	not (B0__bar, B0);
	and (int_fwire_2, A1__bar, B0__bar, C1__bar);
	and (int_fwire_3, A1__bar, B0__bar, C0__bar);
	not (A0__bar, A0);
	and (int_fwire_4, A0__bar, B1__bar, C1__bar);
	and (int_fwire_5, A0__bar, B1__bar, C0__bar);
	and (int_fwire_6, A0__bar, B0__bar, C1__bar);
	and (int_fwire_7, A0__bar, B0__bar, C0__bar);
	or (Y, int_fwire_7, int_fwire_6, int_fwire_5, int_fwire_4, int_fwire_3, int_fwire_2, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if ((A1 & B0 & ~B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((A1 & B0 & ~B1 & ~C0 & C1))
			(A0 => Y) = 0;
		if ((A1 & B0 & ~B1 & ~C0 & ~C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & B1 & ~C0 & C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & B1 & ~C0 & ~C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & ~B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & ~B1 & ~C0 & C1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & ~B1 & ~C0 & ~C1))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((A0 & B0 & ~B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((A0 & B0 & ~B1 & ~C0 & C1))
			(A1 => Y) = 0;
		if ((A0 & B0 & ~B1 & ~C0 & ~C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & B1 & ~C0 & C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & B1 & ~C0 & ~C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & ~B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & ~B1 & ~C0 & C1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & ~B1 & ~C0 & ~C1))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & ~A1 & B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & B1 & ~C0 & C1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & B1 & ~C0 & ~C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & B1 & ~C0 & C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & B1 & ~C0 & ~C1))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & B1 & ~C0 & C1))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & B1 & ~C0 & ~C1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & ~A1 & B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~C0 & C1))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~C0 & ~C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & B0 & ~C0 & C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & B0 & ~C0 & ~C1))
			(B1 => Y) = 0;
		if ((~A0 & ~A1 & B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((~A0 & ~A1 & B0 & ~C0 & C1))
			(B1 => Y) = 0;
		if ((~A0 & ~A1 & B0 & ~C0 & ~C1))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1 & C1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1 & C1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & ~B1 & C1) | (~A0 & ~A1 & B0 & ~B1 & C1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1 & C1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1 & C1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & ~B1 & C1))
			(C0 => Y) = 0;
		if ((~A0 & ~A1 & ~B0 & B1 & C1))
			(C0 => Y) = 0;
		if ((~A0 & ~A1 & ~B0 & ~B1 & C1))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1 & C0))
			(C1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1 & C0))
			(C1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & ~B1 & C0) | (~A0 & ~A1 & B0 & ~B1 & C0))
			(C1 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1 & C0))
			(C1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1 & C0))
			(C1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & ~B1 & C0))
			(C1 => Y) = 0;
		if ((~A0 & ~A1 & ~B0 & B1 & C0))
			(C1 => Y) = 0;
		if ((~A0 & ~A1 & ~B0 & ~B1 & C0))
			(C1 => Y) = 0;
		ifnone (C1 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AOI22 
`timescale 1ns/10ps
`celldefine
module AOI22X1 (Y, A0, A1, B0, B1);
	output Y;
	input A0, A1, B0, B1;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, int_fwire_0, int_fwire_1;
	wire int_fwire_2, int_fwire_3;

	not (B1__bar, B1);
	not (A1__bar, A1);
	and (int_fwire_0, A1__bar, B1__bar);
	not (B0__bar, B0);
	and (int_fwire_1, A1__bar, B0__bar);
	not (A0__bar, A0);
	and (int_fwire_2, A0__bar, B1__bar);
	and (int_fwire_3, A0__bar, B0__bar);
	or (Y, int_fwire_3, int_fwire_2, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if ((A1 & B0 & ~B1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & B1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & ~B1))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((A0 & B0 & ~B1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & B1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & ~B1))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & ~A1 & B1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & B1))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & B1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & ~A1 & B0))
			(B1 => Y) = 0;
		if ((~A0 & A1 & B0))
			(B1 => Y) = 0;
		if ((~A0 & ~A1 & B0))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: AOI22 
`timescale 1ns/10ps
`celldefine
module AOI22X2 (Y, A0, A1, B0, B1);
	output Y;
	input A0, A1, B0, B1;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, int_fwire_0, int_fwire_1;
	wire int_fwire_2, int_fwire_3;

	not (B1__bar, B1);
	not (A1__bar, A1);
	and (int_fwire_0, A1__bar, B1__bar);
	not (B0__bar, B0);
	and (int_fwire_1, A1__bar, B0__bar);
	not (A0__bar, A0);
	and (int_fwire_2, A0__bar, B1__bar);
	and (int_fwire_3, A0__bar, B0__bar);
	or (Y, int_fwire_3, int_fwire_2, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if ((A1 & B0 & ~B1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & B1))
			(A0 => Y) = 0;
		if ((A1 & ~B0 & ~B1))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((A0 & B0 & ~B1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & B1))
			(A1 => Y) = 0;
		if ((A0 & ~B0 & ~B1))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & ~A1 & B1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & B1))
			(B0 => Y) = 0;
		if ((~A0 & ~A1 & B1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & ~A1 & B0))
			(B1 => Y) = 0;
		if ((~A0 & A1 & B0))
			(B1 => Y) = 0;
		if ((~A0 & ~A1 & B0))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: BUF 
`timescale 1ns/10ps
`celldefine
module BUFX16 (Y, A);
	output Y;
	input A;

	// Function
	buf (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: BUF 
`timescale 1ns/10ps
`celldefine
module BUFX2 (Y, A);
	output Y;
	input A;

	// Function
	buf (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: BUF 
`timescale 1ns/10ps
`celldefine
module BUFX4 (Y, A);
	output Y;
	input A;

	// Function
	buf (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: BUF 
`timescale 1ns/10ps
`celldefine
module BUFX8 (Y, A);
	output Y;
	input A;

	// Function
	buf (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: CLKAND2 
`timescale 1ns/10ps
`celldefine
module CLKAND2X2 (Y, A, B);
	output Y;
	input A, B;

	// Function
	and (Y, A, B);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: CLKBUF 
`timescale 1ns/10ps
`celldefine
module CLKBUFX2 (Y, A);
	output Y;
	input A;

	// Function
	buf (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: CLKBUF 
`timescale 1ns/10ps
`celldefine
module CLKBUFX4 (Y, A);
	output Y;
	input A;

	// Function
	buf (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: CLKBUF 
`timescale 1ns/10ps
`celldefine
module CLKBUFX8 (Y, A);
	output Y;
	input A;

	// Function
	buf (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: CLKINV 
`timescale 1ns/10ps
`celldefine
module CLKINVX1 (Y, A);
	output Y;
	input A;

	// Function
	not (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: CLKINV 
`timescale 1ns/10ps
`celldefine
module CLKINVX2 (Y, A);
	output Y;
	input A;

	// Function
	not (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: CLKINV 
`timescale 1ns/10ps
`celldefine
module CLKINVX4 (Y, A);
	output Y;
	input A;

	// Function
	not (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: CLKINV 
`timescale 1ns/10ps
`celldefine
module CLKINVX8 (Y, A);
	output Y;
	input A;

	// Function
	not (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: CLKM 
`timescale 1ns/10ps
`celldefine
module CLKMX2X2 (Y, A, B, S0);
	output Y;
	input A, B, S0;

	// Function
	wire int_fwire_0, int_fwire_1, S0__bar;

	and (int_fwire_0, B, S0);
	not (S0__bar, S0);
	and (int_fwire_1, A, S0__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		if ((~A & B))
			(S0 => Y) = 0;
		ifnone (S0 => Y) = 0;
		if ((A & ~B))
			(S0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: CLKXOR2 
`timescale 1ns/10ps
`celldefine
module CLKXOR2X1 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar, int_fwire_0;
	wire int_fwire_1;

	not (A__bar, A);
	and (int_fwire_0, A__bar, B);
	not (B__bar, B);
	and (int_fwire_1, A, B__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if (~B)
			(A => Y) = 0;
		ifnone (A => Y) = 0;
		if (B)
			(A => Y) = 0;
		if (~A)
			(B => Y) = 0;
		ifnone (B => Y) = 0;
		if (A)
			(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: DFFR 
`timescale 1ns/10ps
`celldefine
module DFFRX1 (Q, QN, CK, D, RN);
	output Q, QN;
	input CK, D, RN;
	reg notifier;
	wire delayed_CK, delayed_D;

	// Function
	wire int_fwire_IQ, int_fwire_IQN, int_fwire_r;
	wire xcr_0;

	not (int_fwire_r, RN);
	altos_dff_r_err (xcr_0, delayed_CK, delayed_D, int_fwire_r);
	altos_dff_r (int_fwire_IQ, notifier, delayed_CK, delayed_D, int_fwire_r, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_D, adacond_RN;


	// Additional timing gates
	buf (adacond_RN, RN);
	buf (adacond_D, D);

	specify
		(posedge CK => (Q+:D)) = 0;
		if (CK)
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & ~D))
			(negedge RN => (Q+:1'b0)) = 0;
		ifnone (negedge RN => (Q+:1'b0)) = 0;
		(posedge CK => (QN-:D)) = 0;
		if (CK)
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & ~D))
			(negedge RN => (QN-:1'b0)) = 0;
		ifnone (negedge RN => (QN-:1'b0)) = 0;
		$setuphold (posedge CK &&& adacond_RN, posedge D &&& adacond_RN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_RN, negedge D &&& adacond_RN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$recovery (posedge RN &&& adacond_D, posedge CK &&& adacond_D, 0, notifier);
		$hold (posedge CK &&& adacond_D, posedge RN &&& adacond_D, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge RN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: DFFR 
`timescale 1ns/10ps
`celldefine
module DFFRX4 (Q, QN, CK, D, RN);
	output Q, QN;
	input CK, D, RN;
	reg notifier;
	wire delayed_CK, delayed_D;

	// Function
	wire int_fwire_IQ, int_fwire_IQN, int_fwire_r;
	wire xcr_0;

	not (int_fwire_r, RN);
	altos_dff_r_err (xcr_0, delayed_CK, delayed_D, int_fwire_r);
	altos_dff_r (int_fwire_IQ, notifier, delayed_CK, delayed_D, int_fwire_r, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_D, adacond_RN;


	// Additional timing gates
	buf (adacond_RN, RN);
	buf (adacond_D, D);

	specify
		(posedge CK => (Q+:D)) = 0;
		if (CK)
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & ~D))
			(negedge RN => (Q+:1'b0)) = 0;
		ifnone (negedge RN => (Q+:1'b0)) = 0;
		(posedge CK => (QN-:D)) = 0;
		if (CK)
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & ~D))
			(negedge RN => (QN-:1'b0)) = 0;
		ifnone (negedge RN => (QN-:1'b0)) = 0;
		$setuphold (posedge CK &&& adacond_RN, posedge D &&& adacond_RN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_RN, negedge D &&& adacond_RN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$recovery (posedge RN &&& adacond_D, posedge CK &&& adacond_D, 0, notifier);
		$hold (posedge CK &&& adacond_D, posedge RN &&& adacond_D, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge RN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: DFFSR 
`timescale 1ns/10ps
`celldefine
module DFFSRX1 (Q, QN, CK, D, RN, SN);
	output Q, QN;
	input CK, D, RN, SN;
	reg notifier;
	wire delayed_CK, delayed_D, delayed_RN, delayed_SN;

	// Function
	wire int_fwire_IQ, int_fwire_IQN, int_fwire_r;
	wire int_fwire_s, xcr_0;

	not (int_fwire_s, delayed_SN);
	not (int_fwire_r, delayed_RN);
	altos_dff_sr_err (xcr_0, delayed_CK, delayed_D, int_fwire_s, int_fwire_r);
	altos_dff_sr_1 (int_fwire_IQ, notifier, delayed_CK, delayed_D, int_fwire_s, int_fwire_r, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_CK, adacond_D_AND_SN, adacond_NOT_CK;
	wire adacond_NOT_D_AND_RN, adacond_RN_AND_SN, D__bar;


	// Additional timing gates
	and (adacond_RN_AND_SN, RN, SN);
	and (adacond_D_AND_SN, D, SN);
	not (D__bar, D);
	and (adacond_NOT_D_AND_RN, D__bar, RN);
	buf (adacond_CK, CK);
	not (adacond_NOT_CK, CK);

	specify
		(posedge CK => (Q+:D)) = 0;
		if ((CK & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & ~D & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		ifnone (negedge RN => (Q+:1'b0)) = 0;
		if ((CK & ~RN))
			(posedge SN => (Q+:1'b0)) = 0;
		if ((~CK & D & ~RN))
			(posedge SN => (Q+:1'b0)) = 0;
		if ((~CK & ~D & ~RN))
			(posedge SN => (Q+:1'b0)) = 0;
		ifnone (posedge SN => (Q+:1'b0)) = 0;
		if ((CK & RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((CK & ~RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & ~RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & ~D & RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & ~D & ~RN))
			(negedge SN => (Q+:1'b1)) = 0;
		ifnone (negedge SN => (Q+:1'b1)) = 0;
		(posedge CK => (QN-:D)) = 0;
		if ((CK & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & ~D & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		ifnone (negedge RN => (QN-:1'b0)) = 0;
		if ((CK & RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((CK & ~RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & ~RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & ~D & RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & ~D & ~RN))
			(negedge SN => (QN-:1'b1)) = 0;
		ifnone (negedge SN => (QN-:1'b1)) = 0;
		if ((CK & ~RN))
			(posedge SN => (QN-:1'b0)) = 0;
		if ((~CK & D & ~RN))
			(posedge SN => (QN-:1'b0)) = 0;
		if ((~CK & ~D & ~RN))
			(posedge SN => (QN-:1'b0)) = 0;
		ifnone (posedge SN => (QN-:1'b0)) = 0;
		$setuphold (posedge CK &&& adacond_RN_AND_SN, posedge D &&& adacond_RN_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_RN_AND_SN, negedge D &&& adacond_RN_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge RN &&& adacond_CK, posedge SN &&& adacond_CK, 0, 0, notifier,,, delayed_RN, delayed_SN);
		$setuphold (posedge RN &&& adacond_NOT_CK, posedge SN &&& adacond_NOT_CK, 0, 0, notifier,,, delayed_RN, delayed_SN);
		$recovery (posedge RN &&& adacond_D_AND_SN, posedge CK &&& adacond_D_AND_SN, 0, notifier);
		$hold (posedge CK &&& adacond_D_AND_SN, posedge RN &&& adacond_D_AND_SN, 0, notifier);
		$recovery (posedge SN &&& adacond_NOT_D_AND_RN, posedge CK &&& adacond_NOT_D_AND_RN, 0, notifier);
		$hold (posedge CK &&& adacond_NOT_D_AND_RN, posedge SN &&& adacond_NOT_D_AND_RN, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge RN, 0, 0, notifier);
		$width (negedge SN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: DFFSR 
`timescale 1ns/10ps
`celldefine
module DFFSRX4 (Q, QN, CK, D, RN, SN);
	output Q, QN;
	input CK, D, RN, SN;
	reg notifier;
	wire delayed_CK, delayed_D, delayed_RN, delayed_SN;

	// Function
	wire int_fwire_IQ, int_fwire_IQN, int_fwire_r;
	wire int_fwire_s, xcr_0;

	not (int_fwire_s, delayed_SN);
	not (int_fwire_r, delayed_RN);
	altos_dff_sr_err (xcr_0, delayed_CK, delayed_D, int_fwire_s, int_fwire_r);
	altos_dff_sr_1 (int_fwire_IQ, notifier, delayed_CK, delayed_D, int_fwire_s, int_fwire_r, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_CK, adacond_D_AND_SN, adacond_NOT_CK;
	wire adacond_NOT_D_AND_RN, adacond_RN_AND_SN, D__bar;


	// Additional timing gates
	and (adacond_RN_AND_SN, RN, SN);
	and (adacond_D_AND_SN, D, SN);
	not (D__bar, D);
	and (adacond_NOT_D_AND_RN, D__bar, RN);
	buf (adacond_CK, CK);
	not (adacond_NOT_CK, CK);

	specify
		(posedge CK => (Q+:D)) = 0;
		if ((CK & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & ~D & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		ifnone (negedge RN => (Q+:1'b0)) = 0;
		if ((CK & ~RN))
			(posedge SN => (Q+:1'b0)) = 0;
		if ((~CK & D & ~RN))
			(posedge SN => (Q+:1'b0)) = 0;
		if ((~CK & ~D & ~RN))
			(posedge SN => (Q+:1'b0)) = 0;
		ifnone (posedge SN => (Q+:1'b0)) = 0;
		if ((CK & RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((CK & ~RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & ~RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & ~D & RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & ~D & ~RN))
			(negedge SN => (Q+:1'b1)) = 0;
		ifnone (negedge SN => (Q+:1'b1)) = 0;
		(posedge CK => (QN-:D)) = 0;
		if ((CK & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & ~D & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		ifnone (negedge RN => (QN-:1'b0)) = 0;
		if ((CK & RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((CK & ~RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & ~RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & ~D & RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & ~D & ~RN))
			(negedge SN => (QN-:1'b1)) = 0;
		ifnone (negedge SN => (QN-:1'b1)) = 0;
		if ((CK & ~RN))
			(posedge SN => (QN-:1'b0)) = 0;
		if ((~CK & D & ~RN))
			(posedge SN => (QN-:1'b0)) = 0;
		if ((~CK & ~D & ~RN))
			(posedge SN => (QN-:1'b0)) = 0;
		ifnone (posedge SN => (QN-:1'b0)) = 0;
		$setuphold (posedge CK &&& adacond_RN_AND_SN, posedge D &&& adacond_RN_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_RN_AND_SN, negedge D &&& adacond_RN_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge RN &&& adacond_CK, posedge SN &&& adacond_CK, 0, 0, notifier,,, delayed_RN, delayed_SN);
		$setuphold (posedge RN &&& adacond_NOT_CK, posedge SN &&& adacond_NOT_CK, 0, 0, notifier,,, delayed_RN, delayed_SN);
		$recovery (posedge RN &&& adacond_D_AND_SN, posedge CK &&& adacond_D_AND_SN, 0, notifier);
		$hold (posedge CK &&& adacond_D_AND_SN, posedge RN &&& adacond_D_AND_SN, 0, notifier);
		$recovery (posedge SN &&& adacond_NOT_D_AND_RN, posedge CK &&& adacond_NOT_D_AND_RN, 0, notifier);
		$hold (posedge CK &&& adacond_NOT_D_AND_RN, posedge SN &&& adacond_NOT_D_AND_RN, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge RN, 0, 0, notifier);
		$width (negedge SN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: DFFS 
`timescale 1ns/10ps
`celldefine
module DFFSX1 (Q, QN, CK, D, SN);
	output Q, QN;
	input CK, D, SN;
	reg notifier;
	wire delayed_CK, delayed_D;

	// Function
	wire int_fwire_IQ, int_fwire_IQN, int_fwire_s;
	wire xcr_0;

	not (int_fwire_s, SN);
	altos_dff_s_err (xcr_0, delayed_CK, delayed_D, int_fwire_s);
	altos_dff_s (int_fwire_IQ, notifier, delayed_CK, delayed_D, int_fwire_s, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_NOT_D, adacond_SN;


	// Additional timing gates
	buf (adacond_SN, SN);
	not (adacond_NOT_D, D);

	specify
		(posedge CK => (Q+:D)) = 0;
		if (CK)
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & ~D))
			(negedge SN => (Q+:1'b1)) = 0;
		ifnone (negedge SN => (Q+:1'b1)) = 0;
		(posedge CK => (QN-:D)) = 0;
		if (CK)
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & ~D))
			(negedge SN => (QN-:1'b1)) = 0;
		ifnone (negedge SN => (QN-:1'b1)) = 0;
		$setuphold (posedge CK &&& adacond_SN, posedge D &&& adacond_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_SN, negedge D &&& adacond_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$recovery (posedge SN &&& adacond_NOT_D, posedge CK &&& adacond_NOT_D, 0, notifier);
		$hold (posedge CK &&& adacond_NOT_D, posedge SN &&& adacond_NOT_D, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge SN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: DFFS 
`timescale 1ns/10ps
`celldefine
module DFFSX4 (Q, QN, CK, D, SN);
	output Q, QN;
	input CK, D, SN;
	reg notifier;
	wire delayed_CK, delayed_D;

	// Function
	wire int_fwire_IQ, int_fwire_IQN, int_fwire_s;
	wire xcr_0;

	not (int_fwire_s, SN);
	altos_dff_s_err (xcr_0, delayed_CK, delayed_D, int_fwire_s);
	altos_dff_s (int_fwire_IQ, notifier, delayed_CK, delayed_D, int_fwire_s, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_NOT_D, adacond_SN;


	// Additional timing gates
	buf (adacond_SN, SN);
	not (adacond_NOT_D, D);

	specify
		(posedge CK => (Q+:D)) = 0;
		if (CK)
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & ~D))
			(negedge SN => (Q+:1'b1)) = 0;
		ifnone (negedge SN => (Q+:1'b1)) = 0;
		(posedge CK => (QN-:D)) = 0;
		if (CK)
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & ~D))
			(negedge SN => (QN-:1'b1)) = 0;
		ifnone (negedge SN => (QN-:1'b1)) = 0;
		$setuphold (posedge CK &&& adacond_SN, posedge D &&& adacond_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_SN, negedge D &&& adacond_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$recovery (posedge SN &&& adacond_NOT_D, posedge CK &&& adacond_NOT_D, 0, notifier);
		$hold (posedge CK &&& adacond_NOT_D, posedge SN &&& adacond_NOT_D, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge SN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: DFF 
`timescale 1ns/10ps
`celldefine
module DFFX1 (Q, QN, CK, D);
	output Q, QN;
	input CK, D;
	reg notifier;
	wire delayed_CK, delayed_D;

	// Function
	wire int_fwire_IQ, int_fwire_IQN, xcr_0;

	altos_dff_err (xcr_0, delayed_CK, delayed_D);
	altos_dff (int_fwire_IQ, notifier, delayed_CK, delayed_D, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing
	specify
		(posedge CK => (Q+:D)) = 0;
		(posedge CK => (QN-:D)) = 0;
		$setuphold (posedge CK, posedge D, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK, negedge D, 0, 0, notifier,,, delayed_CK, delayed_D);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: DFF 
`timescale 1ns/10ps
`celldefine
module DFFX4 (Q, QN, CK, D);
	output Q, QN;
	input CK, D;
	reg notifier;
	wire delayed_CK, delayed_D;

	// Function
	wire int_fwire_IQ, int_fwire_IQN, xcr_0;

	altos_dff_err (xcr_0, delayed_CK, delayed_D);
	altos_dff (int_fwire_IQ, notifier, delayed_CK, delayed_D, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing
	specify
		(posedge CK => (Q+:D)) = 0;
		(posedge CK => (QN-:D)) = 0;
		$setuphold (posedge CK, posedge D, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK, negedge D, 0, 0, notifier,,, delayed_CK, delayed_D);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: DLY1 
`timescale 1ns/10ps
`celldefine
module DLY1X1 (Y, A);
	output Y;
	input A;

	// Function
	buf (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: DLY1 
`timescale 1ns/10ps
`celldefine
module DLY1X4 (Y, A);
	output Y;
	input A;

	// Function
	buf (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: DLY2 
`timescale 1ns/10ps
`celldefine
module DLY2X4 (Y, A);
	output Y;
	input A;

	// Function
	buf (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: DLY4 
`timescale 1ns/10ps
`celldefine
module DLY4X4 (Y, A);
	output Y;
	input A;

	// Function
	buf (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: ICG 
`timescale 1ns/10ps
`celldefine
module ICGX1 (Y, D, G);
	output Y;
	input D, G;
	reg notifier;
	wire delayed_D, delayed_G;

	// Function
        wire  int_fwire_A, int_fwire_B;
   
        altos_latch (int_fwire_A, notifier, delayed_G, delayed_D);
        buf (int_fwire_B, G);   
        and (Y, int_fwire_A, int_fwire_B);
   	     
	// Timing
	specify
		(D => Y) = 0;
		(G => Y) = 0;
		$setuphold (negedge G, posedge D, 0, 0, notifier,,, delayed_G, delayed_D);
		$setuphold (negedge G, negedge D, 0, 0, notifier,,, delayed_G, delayed_D);
		$width (posedge G, 0, 0, notifier);
		$width (negedge G, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: INV 
`timescale 1ns/10ps
`celldefine
module INVX1 (Y, A);
	output Y;
	input A;

	// Function
	not (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: INV 
`timescale 1ns/10ps
`celldefine
module INVX16 (Y, A);
	output Y;
	input A;

	// Function
	not (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: INV 
`timescale 1ns/10ps
`celldefine
module INVX2 (Y, A);
	output Y;
	input A;

	// Function
	not (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: INV 
`timescale 1ns/10ps
`celldefine
module INVX4 (Y, A);
	output Y;
	input A;

	// Function
	not (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: INV 
`timescale 1ns/10ps
`celldefine
module INVX8 (Y, A);
	output Y;
	input A;

	// Function
	not (Y, A);

	// Timing
	specify
		(A => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: MX2 
`timescale 1ns/10ps
`celldefine
module MX2X1 (Y, A, B, S0);
	output Y;
	input A, B, S0;

	// Function
	wire int_fwire_0, int_fwire_1, S0__bar;

	and (int_fwire_0, B, S0);
	not (S0__bar, S0);
	and (int_fwire_1, A, S0__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		if ((~A & B))
			(S0 => Y) = 0;
		ifnone (S0 => Y) = 0;
		if ((A & ~B))
			(S0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: MX2 
`timescale 1ns/10ps
`celldefine
module MX2X4 (Y, A, B, S0);
	output Y;
	input A, B, S0;

	// Function
	wire int_fwire_0, int_fwire_1, S0__bar;

	and (int_fwire_0, B, S0);
	not (S0__bar, S0);
	and (int_fwire_1, A, S0__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		if ((~A & B))
			(S0 => Y) = 0;
		ifnone (S0 => Y) = 0;
		if ((A & ~B))
			(S0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: MX4 
`timescale 1ns/10ps
`celldefine
module MX4X1 (Y, A, B, C, D, S0, S1);
	output Y;
	input A, B, C, D, S0, S1;

	// Function
	wire int_fwire_0, int_fwire_1, int_fwire_2;
	wire int_fwire_3, S0__bar, S1__bar;

	and (int_fwire_0, D, S0, S1);
	not (S0__bar, S0);
	and (int_fwire_1, C, S0__bar, S1);
	not (S1__bar, S1);
	and (int_fwire_2, B, S0, S1__bar);
	and (int_fwire_3, A, S0__bar, S1__bar);
	or (Y, int_fwire_3, int_fwire_2, int_fwire_1, int_fwire_0);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
		if ((A & B & ~C & D & S1) | (~A & ~B & ~C & D & S1))
			(S0 => Y) = 0;
		if ((A & ~B & ~C & D & S1))
			(S0 => Y) = 0;
		if ((~A & B & C & D & ~S1) | (~A & B & ~C & ~D & ~S1))
			(S0 => Y) = 0;
		if ((~A & B & C & ~D & ~S1))
			(S0 => Y) = 0;
		if ((~A & B & ~C & D & S1))
			(S0 => Y) = 0;
		if ((~A & B & ~C & D & ~S1))
			(S0 => Y) = 0;
		ifnone (S0 => Y) = 0;
		if ((A & B & C & ~D & S1) | (~A & ~B & C & ~D & S1))
			(S0 => Y) = 0;
		if ((A & ~B & C & D & ~S1) | (A & ~B & ~C & ~D & ~S1))
			(S0 => Y) = 0;
		if ((A & ~B & C & ~D & S1))
			(S0 => Y) = 0;
		if ((A & ~B & C & ~D & ~S1))
			(S0 => Y) = 0;
		if ((A & ~B & ~C & D & ~S1))
			(S0 => Y) = 0;
		if ((~A & B & C & ~D & S1))
			(S0 => Y) = 0;
		if ((~B & D & S0))
			(S1 => Y) = 0;
		if ((~A & C & ~S0))
			(S1 => Y) = 0;
		ifnone (S1 => Y) = 0;
		if ((B & ~D & S0))
			(S1 => Y) = 0;
		if ((A & ~C & ~S0))
			(S1 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: MX4 
`timescale 1ns/10ps
`celldefine
module MX4X4 (Y, A, B, C, D, S0, S1);
	output Y;
	input A, B, C, D, S0, S1;

	// Function
	wire int_fwire_0, int_fwire_1, int_fwire_2;
	wire int_fwire_3, S0__bar, S1__bar;

	and (int_fwire_0, D, S0, S1);
	not (S0__bar, S0);
	and (int_fwire_1, C, S0__bar, S1);
	not (S1__bar, S1);
	and (int_fwire_2, B, S0, S1__bar);
	and (int_fwire_3, A, S0__bar, S1__bar);
	or (Y, int_fwire_3, int_fwire_2, int_fwire_1, int_fwire_0);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
		if ((A & B & ~C & D & S1) | (~A & ~B & ~C & D & S1))
			(S0 => Y) = 0;
		if ((A & ~B & ~C & D & S1))
			(S0 => Y) = 0;
		if ((~A & B & C & D & ~S1) | (~A & B & ~C & ~D & ~S1))
			(S0 => Y) = 0;
		if ((~A & B & C & ~D & ~S1))
			(S0 => Y) = 0;
		if ((~A & B & ~C & D & S1))
			(S0 => Y) = 0;
		if ((~A & B & ~C & D & ~S1))
			(S0 => Y) = 0;
		ifnone (S0 => Y) = 0;
		if ((A & B & C & ~D & S1) | (~A & ~B & C & ~D & S1))
			(S0 => Y) = 0;
		if ((A & ~B & C & D & ~S1) | (A & ~B & ~C & ~D & ~S1))
			(S0 => Y) = 0;
		if ((A & ~B & C & ~D & S1))
			(S0 => Y) = 0;
		if ((A & ~B & C & ~D & ~S1))
			(S0 => Y) = 0;
		if ((A & ~B & ~C & D & ~S1))
			(S0 => Y) = 0;
		if ((~A & B & C & ~D & S1))
			(S0 => Y) = 0;
		if ((~B & D & S0))
			(S1 => Y) = 0;
		if ((~A & C & ~S0))
			(S1 => Y) = 0;
		ifnone (S1 => Y) = 0;
		if ((B & ~D & S0))
			(S1 => Y) = 0;
		if ((A & ~C & ~S0))
			(S1 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NAND2 
`timescale 1ns/10ps
`celldefine
module NAND2X1 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar;

	not (B__bar, B);
	not (A__bar, A);
	or (Y, A__bar, B__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NAND2 
`timescale 1ns/10ps
`celldefine
module NAND2X2 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar;

	not (B__bar, B);
	not (A__bar, A);
	or (Y, A__bar, B__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NAND2 
`timescale 1ns/10ps
`celldefine
module NAND2X4 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar;

	not (B__bar, B);
	not (A__bar, A);
	or (Y, A__bar, B__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NAND3 
`timescale 1ns/10ps
`celldefine
module NAND3X1 (Y, A, B, C);
	output Y;
	input A, B, C;

	// Function
	wire A__bar, B__bar, C__bar;

	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	or (Y, A__bar, B__bar, C__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NAND3 
`timescale 1ns/10ps
`celldefine
module NAND3X2 (Y, A, B, C);
	output Y;
	input A, B, C;

	// Function
	wire A__bar, B__bar, C__bar;

	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	or (Y, A__bar, B__bar, C__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NAND3 
`timescale 1ns/10ps
`celldefine
module NAND3X4 (Y, A, B, C);
	output Y;
	input A, B, C;

	// Function
	wire A__bar, B__bar, C__bar;

	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	or (Y, A__bar, B__bar, C__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NAND4 
`timescale 1ns/10ps
`celldefine
module NAND4X1 (Y, A, B, C, D);
	output Y;
	input A, B, C, D;

	// Function
	wire A__bar, B__bar, C__bar;
	wire D__bar;

	not (D__bar, D);
	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	or (Y, A__bar, B__bar, C__bar, D__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NAND4 
`timescale 1ns/10ps
`celldefine
module NAND4X2 (Y, A, B, C, D);
	output Y;
	input A, B, C, D;

	// Function
	wire A__bar, B__bar, C__bar;
	wire D__bar;

	not (D__bar, D);
	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	or (Y, A__bar, B__bar, C__bar, D__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NAND4 
`timescale 1ns/10ps
`celldefine
module NAND4X4 (Y, A, B, C, D);
	output Y;
	input A, B, C, D;

	// Function
	wire A__bar, B__bar, C__bar;
	wire D__bar;

	not (D__bar, D);
	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	or (Y, A__bar, B__bar, C__bar, D__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NOR2 
`timescale 1ns/10ps
`celldefine
module NOR2X1 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar;

	not (B__bar, B);
	not (A__bar, A);
	and (Y, A__bar, B__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NOR2 
`timescale 1ns/10ps
`celldefine
module NOR2X2 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar;

	not (B__bar, B);
	not (A__bar, A);
	and (Y, A__bar, B__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NOR2 
`timescale 1ns/10ps
`celldefine
module NOR2X4 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar;

	not (B__bar, B);
	not (A__bar, A);
	and (Y, A__bar, B__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NOR3 
`timescale 1ns/10ps
`celldefine
module NOR3X1 (Y, A, B, C);
	output Y;
	input A, B, C;

	// Function
	wire A__bar, B__bar, C__bar;

	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	and (Y, A__bar, B__bar, C__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NOR3 
`timescale 1ns/10ps
`celldefine
module NOR3X2 (Y, A, B, C);
	output Y;
	input A, B, C;

	// Function
	wire A__bar, B__bar, C__bar;

	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	and (Y, A__bar, B__bar, C__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NOR3 
`timescale 1ns/10ps
`celldefine
module NOR3X4 (Y, A, B, C);
	output Y;
	input A, B, C;

	// Function
	wire A__bar, B__bar, C__bar;

	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	and (Y, A__bar, B__bar, C__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NOR4 
`timescale 1ns/10ps
`celldefine
module NOR4X1 (Y, A, B, C, D);
	output Y;
	input A, B, C, D;

	// Function
	wire A__bar, B__bar, C__bar;
	wire D__bar;

	not (D__bar, D);
	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	and (Y, A__bar, B__bar, C__bar, D__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NOR4 
`timescale 1ns/10ps
`celldefine
module NOR4X2 (Y, A, B, C, D);
	output Y;
	input A, B, C, D;

	// Function
	wire A__bar, B__bar, C__bar;
	wire D__bar;

	not (D__bar, D);
	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	and (Y, A__bar, B__bar, C__bar, D__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: NOR4 
`timescale 1ns/10ps
`celldefine
module NOR4X4 (Y, A, B, C, D);
	output Y;
	input A, B, C, D;

	// Function
	wire A__bar, B__bar, C__bar;
	wire D__bar;

	not (D__bar, D);
	not (C__bar, C);
	not (B__bar, B);
	not (A__bar, A);
	and (Y, A__bar, B__bar, C__bar, D__bar);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OAI211 
`timescale 1ns/10ps
`celldefine
module OAI211X1 (Y, A0, A1, B0, C0);
	output Y;
	input A0, A1, B0, C0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire C0__bar, int_fwire_0;

	not (C0__bar, C0);
	not (B0__bar, B0);
	not (A1__bar, A1);
	not (A0__bar, A0);
	and (int_fwire_0, A0__bar, A1__bar);
	or (Y, int_fwire_0, B0__bar, C0__bar);

	// Timing
	specify
		(A0 => Y) = 0;
		(A1 => Y) = 0;
		if ((A0 & A1 & C0))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & C0))
			(B0 => Y) = 0;
		if ((~A0 & A1 & C0))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & A1 & B0))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & B0))
			(C0 => Y) = 0;
		if ((~A0 & A1 & B0))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OAI211 
`timescale 1ns/10ps
`celldefine
module OAI211X2 (Y, A0, A1, B0, C0);
	output Y;
	input A0, A1, B0, C0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire C0__bar, int_fwire_0;

	not (C0__bar, C0);
	not (B0__bar, B0);
	not (A1__bar, A1);
	not (A0__bar, A0);
	and (int_fwire_0, A0__bar, A1__bar);
	or (Y, int_fwire_0, B0__bar, C0__bar);

	// Timing
	specify
		(A0 => Y) = 0;
		(A1 => Y) = 0;
		if ((A0 & A1 & C0))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & C0))
			(B0 => Y) = 0;
		if ((~A0 & A1 & C0))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & A1 & B0))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & B0))
			(C0 => Y) = 0;
		if ((~A0 & A1 & B0))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OAI21 
`timescale 1ns/10ps
`celldefine
module OAI21X1 (Y, A0, A1, B0);
	output Y;
	input A0, A1, B0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire int_fwire_0;

	not (B0__bar, B0);
	not (A1__bar, A1);
	not (A0__bar, A0);
	and (int_fwire_0, A0__bar, A1__bar);
	or (Y, int_fwire_0, B0__bar);

	// Timing
	specify
		(A0 => Y) = 0;
		(A1 => Y) = 0;
		if ((A0 & A1))
			(B0 => Y) = 0;
		if ((A0 & ~A1))
			(B0 => Y) = 0;
		if ((~A0 & A1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OAI21 
`timescale 1ns/10ps
`celldefine
module OAI21X2 (Y, A0, A1, B0);
	output Y;
	input A0, A1, B0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire int_fwire_0;

	not (B0__bar, B0);
	not (A1__bar, A1);
	not (A0__bar, A0);
	and (int_fwire_0, A0__bar, A1__bar);
	or (Y, int_fwire_0, B0__bar);

	// Timing
	specify
		(A0 => Y) = 0;
		(A1 => Y) = 0;
		if ((A0 & A1))
			(B0 => Y) = 0;
		if ((A0 & ~A1))
			(B0 => Y) = 0;
		if ((~A0 & A1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OAI221 
`timescale 1ns/10ps
`celldefine
module OAI221X1 (Y, A0, A1, B0, B1, C0);
	output Y;
	input A0, A1, B0, B1, C0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, C0__bar, int_fwire_0;
	wire int_fwire_1;

	not (C0__bar, C0);
	not (B1__bar, B1);
	not (B0__bar, B0);
	and (int_fwire_0, B0__bar, B1__bar);
	not (A1__bar, A1);
	not (A0__bar, A0);
	and (int_fwire_1, A0__bar, A1__bar);
	or (Y, int_fwire_1, int_fwire_0, C0__bar);

	// Timing
	specify
		if ((~A1 & B0 & B1 & C0))
			(A0 => Y) = 0;
		if ((~A1 & B0 & ~B1 & C0))
			(A0 => Y) = 0;
		if ((~A1 & ~B0 & B1 & C0))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((~A0 & B0 & B1 & C0))
			(A1 => Y) = 0;
		if ((~A0 & B0 & ~B1 & C0))
			(A1 => Y) = 0;
		if ((~A0 & ~B0 & B1 & C0))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & A1 & ~B1 & C0))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & ~B1 & C0))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~B1 & C0))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & A1 & ~B0 & C0))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & C0))
			(B1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & C0))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
		if ((A0 & A1 & B0 & B1))
			(C0 => Y) = 0;
		if ((A0 & A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((A0 & A1 & ~B0 & B1) | (~A0 & A1 & B0 & B1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & B0 & B1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OAI221 
`timescale 1ns/10ps
`celldefine
module OAI221X2 (Y, A0, A1, B0, B1, C0);
	output Y;
	input A0, A1, B0, B1, C0;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, C0__bar, int_fwire_0;
	wire int_fwire_1;

	not (C0__bar, C0);
	not (B1__bar, B1);
	not (B0__bar, B0);
	and (int_fwire_0, B0__bar, B1__bar);
	not (A1__bar, A1);
	not (A0__bar, A0);
	and (int_fwire_1, A0__bar, A1__bar);
	or (Y, int_fwire_1, int_fwire_0, C0__bar);

	// Timing
	specify
		if ((~A1 & B0 & B1 & C0))
			(A0 => Y) = 0;
		if ((~A1 & B0 & ~B1 & C0))
			(A0 => Y) = 0;
		if ((~A1 & ~B0 & B1 & C0))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((~A0 & B0 & B1 & C0))
			(A1 => Y) = 0;
		if ((~A0 & B0 & ~B1 & C0))
			(A1 => Y) = 0;
		if ((~A0 & ~B0 & B1 & C0))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & A1 & ~B1 & C0))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & ~B1 & C0))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~B1 & C0))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & A1 & ~B0 & C0))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & C0))
			(B1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & C0))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
		if ((A0 & A1 & B0 & B1))
			(C0 => Y) = 0;
		if ((A0 & A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((A0 & A1 & ~B0 & B1) | (~A0 & A1 & B0 & B1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & B0 & B1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OAI222 
`timescale 1ns/10ps
`celldefine
module OAI222X1 (Y, A0, A1, B0, B1, C0, C1);
	output Y;
	input A0, A1, B0, B1, C0, C1;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, C0__bar, C1__bar;
	wire int_fwire_0, int_fwire_1, int_fwire_2;

	not (C1__bar, C1);
	not (C0__bar, C0);
	and (int_fwire_0, C0__bar, C1__bar);
	not (B1__bar, B1);
	not (B0__bar, B0);
	and (int_fwire_1, B0__bar, B1__bar);
	not (A1__bar, A1);
	not (A0__bar, A0);
	and (int_fwire_2, A0__bar, A1__bar);
	or (Y, int_fwire_2, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if ((~A1 & B0 & B1 & C0 & C1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & B1 & ~C0 & C1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & ~B1 & C0 & C1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & ~B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & ~B1 & ~C0 & C1))
			(A0 => Y) = 0;
		if ((~A1 & ~B0 & B1 & C0 & C1))
			(A0 => Y) = 0;
		if ((~A1 & ~B0 & B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((~A1 & ~B0 & B1 & ~C0 & C1))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((~A0 & B0 & B1 & C0 & C1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & B1 & ~C0 & C1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & ~B1 & C0 & C1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & ~B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & ~B1 & ~C0 & C1))
			(A1 => Y) = 0;
		if ((~A0 & ~B0 & B1 & C0 & C1))
			(A1 => Y) = 0;
		if ((~A0 & ~B0 & B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((~A0 & ~B0 & B1 & ~C0 & C1))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & A1 & ~B1 & C0 & C1))
			(B0 => Y) = 0;
		if ((A0 & A1 & ~B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((A0 & A1 & ~B1 & ~C0 & C1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & ~B1 & C0 & C1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & ~B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & ~B1 & ~C0 & C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~B1 & C0 & C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~B1 & ~C0 & C1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & A1 & ~B0 & C0 & C1))
			(B1 => Y) = 0;
		if ((A0 & A1 & ~B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((A0 & A1 & ~B0 & ~C0 & C1))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & C0 & C1))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & ~C0 & C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & C0 & C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & ~C0 & C1))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
		if ((A0 & A1 & B0 & B1 & ~C1))
			(C0 => Y) = 0;
		if ((A0 & A1 & B0 & ~B1 & ~C1))
			(C0 => Y) = 0;
		if ((A0 & A1 & ~B0 & B1 & ~C1) | (~A0 & A1 & B0 & B1 & ~C1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & B0 & B1 & ~C1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1 & ~C1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1 & ~C1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1 & ~C1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1 & ~C1))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
		if ((A0 & A1 & B0 & B1 & ~C0))
			(C1 => Y) = 0;
		if ((A0 & A1 & B0 & ~B1 & ~C0))
			(C1 => Y) = 0;
		if ((A0 & A1 & ~B0 & B1 & ~C0) | (~A0 & A1 & B0 & B1 & ~C0))
			(C1 => Y) = 0;
		if ((A0 & ~A1 & B0 & B1 & ~C0))
			(C1 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1 & ~C0))
			(C1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1 & ~C0))
			(C1 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1 & ~C0))
			(C1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1 & ~C0))
			(C1 => Y) = 0;
		ifnone (C1 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OAI222 
`timescale 1ns/10ps
`celldefine
module OAI222X2 (Y, A0, A1, B0, B1, C0, C1);
	output Y;
	input A0, A1, B0, B1, C0, C1;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, C0__bar, C1__bar;
	wire int_fwire_0, int_fwire_1, int_fwire_2;

	not (C1__bar, C1);
	not (C0__bar, C0);
	and (int_fwire_0, C0__bar, C1__bar);
	not (B1__bar, B1);
	not (B0__bar, B0);
	and (int_fwire_1, B0__bar, B1__bar);
	not (A1__bar, A1);
	not (A0__bar, A0);
	and (int_fwire_2, A0__bar, A1__bar);
	or (Y, int_fwire_2, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if ((~A1 & B0 & B1 & C0 & C1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & B1 & ~C0 & C1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & ~B1 & C0 & C1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & ~B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & ~B1 & ~C0 & C1))
			(A0 => Y) = 0;
		if ((~A1 & ~B0 & B1 & C0 & C1))
			(A0 => Y) = 0;
		if ((~A1 & ~B0 & B1 & C0 & ~C1))
			(A0 => Y) = 0;
		if ((~A1 & ~B0 & B1 & ~C0 & C1))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((~A0 & B0 & B1 & C0 & C1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & B1 & ~C0 & C1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & ~B1 & C0 & C1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & ~B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & ~B1 & ~C0 & C1))
			(A1 => Y) = 0;
		if ((~A0 & ~B0 & B1 & C0 & C1))
			(A1 => Y) = 0;
		if ((~A0 & ~B0 & B1 & C0 & ~C1))
			(A1 => Y) = 0;
		if ((~A0 & ~B0 & B1 & ~C0 & C1))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & A1 & ~B1 & C0 & C1))
			(B0 => Y) = 0;
		if ((A0 & A1 & ~B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((A0 & A1 & ~B1 & ~C0 & C1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & ~B1 & C0 & C1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & ~B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & ~B1 & ~C0 & C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~B1 & C0 & C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~B1 & C0 & ~C1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~B1 & ~C0 & C1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & A1 & ~B0 & C0 & C1))
			(B1 => Y) = 0;
		if ((A0 & A1 & ~B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((A0 & A1 & ~B0 & ~C0 & C1))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & C0 & C1))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & ~C0 & C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & C0 & C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & C0 & ~C1))
			(B1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & ~C0 & C1))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
		if ((A0 & A1 & B0 & B1 & ~C1))
			(C0 => Y) = 0;
		if ((A0 & A1 & B0 & ~B1 & ~C1))
			(C0 => Y) = 0;
		if ((A0 & A1 & ~B0 & B1 & ~C1) | (~A0 & A1 & B0 & B1 & ~C1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & B0 & B1 & ~C1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1 & ~C1))
			(C0 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1 & ~C1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1 & ~C1))
			(C0 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1 & ~C1))
			(C0 => Y) = 0;
		ifnone (C0 => Y) = 0;
		if ((A0 & A1 & B0 & B1 & ~C0))
			(C1 => Y) = 0;
		if ((A0 & A1 & B0 & ~B1 & ~C0))
			(C1 => Y) = 0;
		if ((A0 & A1 & ~B0 & B1 & ~C0) | (~A0 & A1 & B0 & B1 & ~C0))
			(C1 => Y) = 0;
		if ((A0 & ~A1 & B0 & B1 & ~C0))
			(C1 => Y) = 0;
		if ((A0 & ~A1 & B0 & ~B1 & ~C0))
			(C1 => Y) = 0;
		if ((A0 & ~A1 & ~B0 & B1 & ~C0))
			(C1 => Y) = 0;
		if ((~A0 & A1 & B0 & ~B1 & ~C0))
			(C1 => Y) = 0;
		if ((~A0 & A1 & ~B0 & B1 & ~C0))
			(C1 => Y) = 0;
		ifnone (C1 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OAI22 
`timescale 1ns/10ps
`celldefine
module OAI22X1 (Y, A0, A1, B0, B1);
	output Y;
	input A0, A1, B0, B1;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, int_fwire_0, int_fwire_1;

	not (B1__bar, B1);
	not (B0__bar, B0);
	and (int_fwire_0, B0__bar, B1__bar);
	not (A1__bar, A1);
	not (A0__bar, A0);
	and (int_fwire_1, A0__bar, A1__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if ((~A1 & B0 & B1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & ~B1))
			(A0 => Y) = 0;
		if ((~A1 & ~B0 & B1))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((~A0 & B0 & B1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & ~B1))
			(A1 => Y) = 0;
		if ((~A0 & ~B0 & B1))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & A1 & ~B1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & ~B1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~B1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & A1 & ~B0))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & ~B0))
			(B1 => Y) = 0;
		if ((~A0 & A1 & ~B0))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OAI22 
`timescale 1ns/10ps
`celldefine
module OAI22X2 (Y, A0, A1, B0, B1);
	output Y;
	input A0, A1, B0, B1;

	// Function
	wire A0__bar, A1__bar, B0__bar;
	wire B1__bar, int_fwire_0, int_fwire_1;

	not (B1__bar, B1);
	not (B0__bar, B0);
	and (int_fwire_0, B0__bar, B1__bar);
	not (A1__bar, A1);
	not (A0__bar, A0);
	and (int_fwire_1, A0__bar, A1__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if ((~A1 & B0 & B1))
			(A0 => Y) = 0;
		if ((~A1 & B0 & ~B1))
			(A0 => Y) = 0;
		if ((~A1 & ~B0 & B1))
			(A0 => Y) = 0;
		ifnone (A0 => Y) = 0;
		if ((~A0 & B0 & B1))
			(A1 => Y) = 0;
		if ((~A0 & B0 & ~B1))
			(A1 => Y) = 0;
		if ((~A0 & ~B0 & B1))
			(A1 => Y) = 0;
		ifnone (A1 => Y) = 0;
		if ((A0 & A1 & ~B1))
			(B0 => Y) = 0;
		if ((A0 & ~A1 & ~B1))
			(B0 => Y) = 0;
		if ((~A0 & A1 & ~B1))
			(B0 => Y) = 0;
		ifnone (B0 => Y) = 0;
		if ((A0 & A1 & ~B0))
			(B1 => Y) = 0;
		if ((A0 & ~A1 & ~B0))
			(B1 => Y) = 0;
		if ((~A0 & A1 & ~B0))
			(B1 => Y) = 0;
		ifnone (B1 => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OR2 
`timescale 1ns/10ps
`celldefine
module OR2X1 (Y, A, B);
	output Y;
	input A, B;

	// Function
	or (Y, A, B);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OR2 
`timescale 1ns/10ps
`celldefine
module OR2X2 (Y, A, B);
	output Y;
	input A, B;

	// Function
	or (Y, A, B);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OR3 
`timescale 1ns/10ps
`celldefine
module OR3X1 (Y, A, B, C);
	output Y;
	input A, B, C;

	// Function
	or (Y, A, B, C);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OR3 
`timescale 1ns/10ps
`celldefine
module OR3X2 (Y, A, B, C);
	output Y;
	input A, B, C;

	// Function
	or (Y, A, B, C);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OR4 
`timescale 1ns/10ps
`celldefine
module OR4X1 (Y, A, B, C, D);
	output Y;
	input A, B, C, D;

	// Function
	or (Y, A, B, C, D);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: OR4 
`timescale 1ns/10ps
`celldefine
module OR4X2 (Y, A, B, C, D);
	output Y;
	input A, B, C, D;

	// Function
	or (Y, A, B, C, D);

	// Timing
	specify
		(A => Y) = 0;
		(B => Y) = 0;
		(C => Y) = 0;
		(D => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: SDFFR 
`timescale 1ns/10ps
`celldefine
module SDFFRX1 (Q, QN, CK, D, RN, SE, SI);
	output Q, QN;
	input CK, D, RN, SE, SI;
	reg notifier;
	wire delayed_CK, delayed_D, delayed_SE, delayed_SI;

	// Function
	wire delayed_SE__bar, int_fwire_0, int_fwire_1;
	wire int_fwire_2, int_fwire_d, int_fwire_IQ;
	wire int_fwire_IQN, int_fwire_r, xcr_0;

	and (int_fwire_0, delayed_SE, delayed_SI);
	and (int_fwire_1, delayed_D, delayed_SI);
	not (delayed_SE__bar, delayed_SE);
	and (int_fwire_2, delayed_D, delayed_SE__bar);
	or (int_fwire_d, int_fwire_2, int_fwire_1, int_fwire_0);
	not (int_fwire_r, RN);
	altos_dff_r_err (xcr_0, delayed_CK, int_fwire_d, int_fwire_r);
	altos_dff_r (int_fwire_IQ, notifier, delayed_CK, int_fwire_d, int_fwire_r, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_D_AND_NOT_SE, adacond_D_AND_RN_AND_NOT_SI, adacond_NOT_D_AND_RN_AND_SI;
	wire adacond_RN_AND_NOT_SE, adacond_RN_AND_SE, adacond_SE_AND_SI;
	wire D__bar, SE__bar, SI__bar;


	// Additional timing gates
	not (SE__bar, SE);
	and (adacond_RN_AND_NOT_SE, RN, SE__bar);
	and (adacond_SE_AND_SI, SE, SI);
	and (adacond_D_AND_NOT_SE, D, SE__bar);
	not (SI__bar, SI);
	and (adacond_D_AND_RN_AND_NOT_SI, D, RN, SI__bar);
	not (D__bar, D);
	and (adacond_NOT_D_AND_RN_AND_SI, D__bar, RN, SI);
	and (adacond_RN_AND_SE, RN, SE);

	specify
		if ((RN & SE))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if ((RN & ~SE))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if (CK)
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D & SE & SI) | (~CK & D & ~SE) | (~CK & ~D & SE & SI))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D & SE & ~SI) | (~CK & ~D & SE & ~SI) | (~CK & ~D & ~SE))
			(negedge RN => (Q+:1'b0)) = 0;
		ifnone (negedge RN => (Q+:1'b0)) = 0;
		if ((RN & SE))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if ((RN & ~SE))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if (CK)
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D & SE & SI) | (~CK & D & ~SE) | (~CK & ~D & SE & SI))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D & SE & ~SI) | (~CK & ~D & SE & ~SI) | (~CK & ~D & ~SE))
			(negedge RN => (QN-:1'b0)) = 0;
		ifnone (negedge RN => (QN-:1'b0)) = 0;
		$setuphold (posedge CK &&& adacond_RN_AND_NOT_SE, posedge D &&& adacond_RN_AND_NOT_SE, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_RN_AND_NOT_SE, negedge D &&& adacond_RN_AND_NOT_SE, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_D_AND_RN_AND_NOT_SI, posedge SE &&& adacond_D_AND_RN_AND_NOT_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_D_AND_RN_AND_NOT_SI, negedge SE &&& adacond_D_AND_RN_AND_NOT_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_RN_AND_SI, posedge SE &&& adacond_NOT_D_AND_RN_AND_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_RN_AND_SI, negedge SE &&& adacond_NOT_D_AND_RN_AND_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_RN_AND_SE, posedge SI &&& adacond_RN_AND_SE, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$setuphold (posedge CK &&& adacond_RN_AND_SE, negedge SI &&& adacond_RN_AND_SE, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$recovery (posedge RN &&& adacond_SE_AND_SI, posedge CK &&& adacond_SE_AND_SI, 0, notifier);
		$recovery (posedge RN &&& adacond_D_AND_NOT_SE, posedge CK &&& adacond_D_AND_NOT_SE, 0, notifier);
		$hold (posedge CK &&& adacond_SE_AND_SI, posedge RN &&& adacond_SE_AND_SI, 0, notifier);
		$hold (posedge CK &&& adacond_D_AND_NOT_SE, posedge RN &&& adacond_D_AND_NOT_SE, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge RN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: SDFFR 
`timescale 1ns/10ps
`celldefine
module SDFFRX4 (Q, QN, CK, D, RN, SE, SI);
	output Q, QN;
	input CK, D, RN, SE, SI;
	reg notifier;
	wire delayed_CK, delayed_D, delayed_SE, delayed_SI;

	// Function
	wire delayed_SE__bar, int_fwire_0, int_fwire_1;
	wire int_fwire_2, int_fwire_d, int_fwire_IQ;
	wire int_fwire_IQN, int_fwire_r, xcr_0;

	and (int_fwire_0, delayed_SE, delayed_SI);
	and (int_fwire_1, delayed_D, delayed_SI);
	not (delayed_SE__bar, delayed_SE);
	and (int_fwire_2, delayed_D, delayed_SE__bar);
	or (int_fwire_d, int_fwire_2, int_fwire_1, int_fwire_0);
	not (int_fwire_r, RN);
	altos_dff_r_err (xcr_0, delayed_CK, int_fwire_d, int_fwire_r);
	altos_dff_r (int_fwire_IQ, notifier, delayed_CK, int_fwire_d, int_fwire_r, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_D_AND_NOT_SE, adacond_D_AND_RN_AND_NOT_SI, adacond_NOT_D_AND_RN_AND_SI;
	wire adacond_RN_AND_NOT_SE, adacond_RN_AND_SE, adacond_SE_AND_SI;
	wire D__bar, SE__bar, SI__bar;


	// Additional timing gates
	not (SE__bar, SE);
	and (adacond_RN_AND_NOT_SE, RN, SE__bar);
	and (adacond_SE_AND_SI, SE, SI);
	and (adacond_D_AND_NOT_SE, D, SE__bar);
	not (SI__bar, SI);
	and (adacond_D_AND_RN_AND_NOT_SI, D, RN, SI__bar);
	not (D__bar, D);
	and (adacond_NOT_D_AND_RN_AND_SI, D__bar, RN, SI);
	and (adacond_RN_AND_SE, RN, SE);

	specify
		if ((RN & SE))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if ((RN & ~SE))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if (CK)
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D & SE & SI) | (~CK & D & ~SE) | (~CK & ~D & SE & SI))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D & SE & ~SI) | (~CK & ~D & SE & ~SI) | (~CK & ~D & ~SE))
			(negedge RN => (Q+:1'b0)) = 0;
		ifnone (negedge RN => (Q+:1'b0)) = 0;
		if ((RN & SE))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if ((RN & ~SE))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if (CK)
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D & SE & SI) | (~CK & D & ~SE) | (~CK & ~D & SE & SI))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D & SE & ~SI) | (~CK & ~D & SE & ~SI) | (~CK & ~D & ~SE))
			(negedge RN => (QN-:1'b0)) = 0;
		ifnone (negedge RN => (QN-:1'b0)) = 0;
		$setuphold (posedge CK &&& adacond_RN_AND_NOT_SE, posedge D &&& adacond_RN_AND_NOT_SE, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_RN_AND_NOT_SE, negedge D &&& adacond_RN_AND_NOT_SE, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_D_AND_RN_AND_NOT_SI, posedge SE &&& adacond_D_AND_RN_AND_NOT_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_D_AND_RN_AND_NOT_SI, negedge SE &&& adacond_D_AND_RN_AND_NOT_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_RN_AND_SI, posedge SE &&& adacond_NOT_D_AND_RN_AND_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_RN_AND_SI, negedge SE &&& adacond_NOT_D_AND_RN_AND_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_RN_AND_SE, posedge SI &&& adacond_RN_AND_SE, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$setuphold (posedge CK &&& adacond_RN_AND_SE, negedge SI &&& adacond_RN_AND_SE, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$recovery (posedge RN &&& adacond_SE_AND_SI, posedge CK &&& adacond_SE_AND_SI, 0, notifier);
		$recovery (posedge RN &&& adacond_D_AND_NOT_SE, posedge CK &&& adacond_D_AND_NOT_SE, 0, notifier);
		$hold (posedge CK &&& adacond_SE_AND_SI, posedge RN &&& adacond_SE_AND_SI, 0, notifier);
		$hold (posedge CK &&& adacond_D_AND_NOT_SE, posedge RN &&& adacond_D_AND_NOT_SE, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge RN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: SDFFSR 
`timescale 1ns/10ps
`celldefine
module SDFFSRX1 (Q, QN, CK, D, RN, SE, SI, SN);
	output Q, QN;
	input CK, D, RN, SE, SI, SN;
	reg notifier;
	wire delayed_CK, delayed_D, delayed_RN, delayed_SE, delayed_SI, delayed_SN;

	// Function
	wire delayed_SE__bar, int_fwire_0, int_fwire_1;
	wire int_fwire_2, int_fwire_d, int_fwire_IQ;
	wire int_fwire_IQN, int_fwire_r, int_fwire_s;
	wire xcr_0;

	and (int_fwire_0, delayed_SE, delayed_SI);
	and (int_fwire_1, delayed_D, delayed_SI);
	not (delayed_SE__bar, delayed_SE);
	and (int_fwire_2, delayed_D, delayed_SE__bar);
	or (int_fwire_d, int_fwire_2, int_fwire_1, int_fwire_0);
	not (int_fwire_s, delayed_SN);
	not (int_fwire_r, delayed_RN);
	altos_dff_sr_err (xcr_0, delayed_CK, int_fwire_d, int_fwire_s, int_fwire_r);
	altos_dff_sr_1 (int_fwire_IQ, notifier, delayed_CK, int_fwire_d, int_fwire_s, int_fwire_r, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_CK, adacond_D_AND_NOT_SE_AND_SN, adacond_D_AND_RN_AND_NOT_SI_AND_SN;
	wire adacond_NOT_CK, adacond_NOT_D_AND_RN_AND_NOT_SE, adacond_NOT_D_AND_RN_AND_SI_AND_SN;
	wire adacond_RN_AND_NOT_SE_AND_SN, adacond_RN_AND_SE_AND_NOT_SI, adacond_RN_AND_SE_AND_SN;
	wire adacond_SE_AND_SI_AND_SN, D__bar, SE__bar;
	wire SI__bar;


	// Additional timing gates
	not (SE__bar, SE);
	and (adacond_RN_AND_NOT_SE_AND_SN, RN, SE__bar, SN);
	and (adacond_SE_AND_SI_AND_SN, SE, SI, SN);
	and (adacond_D_AND_NOT_SE_AND_SN, D, SE__bar, SN);
	not (SI__bar, SI);
	and (adacond_D_AND_RN_AND_NOT_SI_AND_SN, D, RN, SI__bar, SN);
	not (D__bar, D);
	and (adacond_NOT_D_AND_RN_AND_SI_AND_SN, D__bar, RN, SI, SN);
	and (adacond_RN_AND_SE_AND_SN, RN, SE, SN);
	and (adacond_RN_AND_SE_AND_NOT_SI, RN, SE, SI__bar);
	and (adacond_NOT_D_AND_RN_AND_NOT_SE, D__bar, RN, SE__bar);
	buf (adacond_CK, CK);
	not (adacond_NOT_CK, CK);

	specify
		if ((RN & SE & SN))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if ((RN & ~SE & SN))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if ((CK & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D & SE & SI & SN) | (~CK & D & ~SE & SN) | (~CK & ~D & SE & SI & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D & SE & ~SI & SN) | (~CK & ~D & SE & ~SI & SN) | (~CK & ~D & ~SE & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		ifnone (negedge RN => (Q+:1'b0)) = 0;
		if ((CK & ~RN))
			(posedge SN => (Q+:1'b0)) = 0;
		if ((~CK & D & ~RN & SE & SI) | (~CK & D & ~RN & ~SE) | (~CK & ~D & ~RN & SE & SI))
			(posedge SN => (Q+:1'b0)) = 0;
		if ((~CK & D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & ~SE))
			(posedge SN => (Q+:1'b0)) = 0;
		ifnone (posedge SN => (Q+:1'b0)) = 0;
		if ((CK & RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((CK & ~RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & RN & SE & SI) | (~CK & D & RN & ~SE) | (~CK & ~D & RN & SE & SI))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & RN & SE & ~SI) | (~CK & ~D & RN & SE & ~SI) | (~CK & ~D & RN & ~SE))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & ~RN & SE & SI) | (~CK & D & ~RN & ~SE) | (~CK & ~D & ~RN & SE & SI))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & ~SE))
			(negedge SN => (Q+:1'b1)) = 0;
		ifnone (negedge SN => (Q+:1'b1)) = 0;
		if ((RN & SE & SN))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if ((RN & ~SE & SN))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if ((CK & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D & SE & SI & SN) | (~CK & D & ~SE & SN) | (~CK & ~D & SE & SI & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D & SE & ~SI & SN) | (~CK & ~D & SE & ~SI & SN) | (~CK & ~D & ~SE & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		ifnone (negedge RN => (QN-:1'b0)) = 0;
		if ((CK & RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((CK & ~RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & RN & SE & SI) | (~CK & D & RN & ~SE) | (~CK & ~D & RN & SE & SI))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & RN & SE & ~SI) | (~CK & ~D & RN & SE & ~SI) | (~CK & ~D & RN & ~SE))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & ~RN & SE & SI) | (~CK & D & ~RN & ~SE) | (~CK & ~D & ~RN & SE & SI))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & ~SE))
			(negedge SN => (QN-:1'b1)) = 0;
		ifnone (negedge SN => (QN-:1'b1)) = 0;
		if ((CK & ~RN))
			(posedge SN => (QN-:1'b0)) = 0;
		if ((~CK & D & ~RN & SE & SI) | (~CK & D & ~RN & ~SE) | (~CK & ~D & ~RN & SE & SI))
			(posedge SN => (QN-:1'b0)) = 0;
		if ((~CK & D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & ~SE))
			(posedge SN => (QN-:1'b0)) = 0;
		ifnone (posedge SN => (QN-:1'b0)) = 0;
		$setuphold (posedge CK &&& adacond_RN_AND_NOT_SE_AND_SN, posedge D &&& adacond_RN_AND_NOT_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_RN_AND_NOT_SE_AND_SN, negedge D &&& adacond_RN_AND_NOT_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_D_AND_RN_AND_NOT_SI_AND_SN, posedge SE &&& adacond_D_AND_RN_AND_NOT_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_D_AND_RN_AND_NOT_SI_AND_SN, negedge SE &&& adacond_D_AND_RN_AND_NOT_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_RN_AND_SI_AND_SN, posedge SE &&& adacond_NOT_D_AND_RN_AND_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_RN_AND_SI_AND_SN, negedge SE &&& adacond_NOT_D_AND_RN_AND_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_RN_AND_SE_AND_SN, posedge SI &&& adacond_RN_AND_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$setuphold (posedge CK &&& adacond_RN_AND_SE_AND_SN, negedge SI &&& adacond_RN_AND_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$setuphold (posedge RN &&& adacond_CK, posedge SN &&& adacond_CK, 0, 0, notifier,,, delayed_RN, delayed_SN);
		$setuphold (posedge RN &&& adacond_NOT_CK, posedge SN &&& adacond_NOT_CK, 0, 0, notifier,,, delayed_RN, delayed_SN);
		$recovery (posedge RN &&& adacond_SE_AND_SI_AND_SN, posedge CK &&& adacond_SE_AND_SI_AND_SN, 0, notifier);
		$recovery (posedge RN &&& adacond_D_AND_NOT_SE_AND_SN, posedge CK &&& adacond_D_AND_NOT_SE_AND_SN, 0, notifier);
		$hold (posedge CK &&& adacond_SE_AND_SI_AND_SN, posedge RN &&& adacond_SE_AND_SI_AND_SN, 0, notifier);
		$hold (posedge CK &&& adacond_D_AND_NOT_SE_AND_SN, posedge RN &&& adacond_D_AND_NOT_SE_AND_SN, 0, notifier);
		$recovery (posedge SN &&& adacond_RN_AND_SE_AND_NOT_SI, posedge CK &&& adacond_RN_AND_SE_AND_NOT_SI, 0, notifier);
		$recovery (posedge SN &&& adacond_NOT_D_AND_RN_AND_NOT_SE, posedge CK &&& adacond_NOT_D_AND_RN_AND_NOT_SE, 0, notifier);
		$hold (posedge CK &&& adacond_RN_AND_SE_AND_NOT_SI, posedge SN &&& adacond_RN_AND_SE_AND_NOT_SI, 0, notifier);
		$hold (posedge CK &&& adacond_NOT_D_AND_RN_AND_NOT_SE, posedge SN &&& adacond_NOT_D_AND_RN_AND_NOT_SE, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge RN, 0, 0, notifier);
		$width (negedge SN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: SDFFSR 
`timescale 1ns/10ps
`celldefine
module SDFFSRX4 (Q, QN, CK, D, RN, SE, SI, SN);
	output Q, QN;
	input CK, D, RN, SE, SI, SN;
	reg notifier;
	wire delayed_CK, delayed_D, delayed_RN, delayed_SE, delayed_SI, delayed_SN;

	// Function
	wire delayed_SE__bar, int_fwire_0, int_fwire_1;
	wire int_fwire_2, int_fwire_d, int_fwire_IQ;
	wire int_fwire_IQN, int_fwire_r, int_fwire_s;
	wire xcr_0;

	and (int_fwire_0, delayed_SE, delayed_SI);
	and (int_fwire_1, delayed_D, delayed_SI);
	not (delayed_SE__bar, delayed_SE);
	and (int_fwire_2, delayed_D, delayed_SE__bar);
	or (int_fwire_d, int_fwire_2, int_fwire_1, int_fwire_0);
	not (int_fwire_s, delayed_SN);
	not (int_fwire_r, delayed_RN);
	altos_dff_sr_err (xcr_0, delayed_CK, int_fwire_d, int_fwire_s, int_fwire_r);
	altos_dff_sr_1 (int_fwire_IQ, notifier, delayed_CK, int_fwire_d, int_fwire_s, int_fwire_r, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_CK, adacond_D_AND_NOT_SE_AND_SN, adacond_D_AND_RN_AND_NOT_SI_AND_SN;
	wire adacond_NOT_CK, adacond_NOT_D_AND_RN_AND_NOT_SE, adacond_NOT_D_AND_RN_AND_SI_AND_SN;
	wire adacond_RN_AND_NOT_SE_AND_SN, adacond_RN_AND_SE_AND_NOT_SI, adacond_RN_AND_SE_AND_SN;
	wire adacond_SE_AND_SI_AND_SN, D__bar, SE__bar;
	wire SI__bar;


	// Additional timing gates
	not (SE__bar, SE);
	and (adacond_RN_AND_NOT_SE_AND_SN, RN, SE__bar, SN);
	and (adacond_SE_AND_SI_AND_SN, SE, SI, SN);
	and (adacond_D_AND_NOT_SE_AND_SN, D, SE__bar, SN);
	not (SI__bar, SI);
	and (adacond_D_AND_RN_AND_NOT_SI_AND_SN, D, RN, SI__bar, SN);
	not (D__bar, D);
	and (adacond_NOT_D_AND_RN_AND_SI_AND_SN, D__bar, RN, SI, SN);
	and (adacond_RN_AND_SE_AND_SN, RN, SE, SN);
	and (adacond_RN_AND_SE_AND_NOT_SI, RN, SE, SI__bar);
	and (adacond_NOT_D_AND_RN_AND_NOT_SE, D__bar, RN, SE__bar);
	buf (adacond_CK, CK);
	not (adacond_NOT_CK, CK);

	specify
		if ((RN & SE & SN))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if ((RN & ~SE & SN))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if ((CK & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D & SE & SI & SN) | (~CK & D & ~SE & SN) | (~CK & ~D & SE & SI & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~CK & D & SE & ~SI & SN) | (~CK & ~D & SE & ~SI & SN) | (~CK & ~D & ~SE & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		ifnone (negedge RN => (Q+:1'b0)) = 0;
		if ((CK & ~RN))
			(posedge SN => (Q+:1'b0)) = 0;
		if ((~CK & D & ~RN & SE & SI) | (~CK & D & ~RN & ~SE) | (~CK & ~D & ~RN & SE & SI))
			(posedge SN => (Q+:1'b0)) = 0;
		if ((~CK & D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & ~SE))
			(posedge SN => (Q+:1'b0)) = 0;
		ifnone (posedge SN => (Q+:1'b0)) = 0;
		if ((CK & RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((CK & ~RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & RN & SE & SI) | (~CK & D & RN & ~SE) | (~CK & ~D & RN & SE & SI))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & RN & SE & ~SI) | (~CK & ~D & RN & SE & ~SI) | (~CK & ~D & RN & ~SE))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & ~RN & SE & SI) | (~CK & D & ~RN & ~SE) | (~CK & ~D & ~RN & SE & SI))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & ~SE))
			(negedge SN => (Q+:1'b1)) = 0;
		ifnone (negedge SN => (Q+:1'b1)) = 0;
		if ((RN & SE & SN))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if ((RN & ~SE & SN))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if ((CK & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D & SE & SI & SN) | (~CK & D & ~SE & SN) | (~CK & ~D & SE & SI & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~CK & D & SE & ~SI & SN) | (~CK & ~D & SE & ~SI & SN) | (~CK & ~D & ~SE & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		ifnone (negedge RN => (QN-:1'b0)) = 0;
		if ((CK & RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((CK & ~RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & RN & SE & SI) | (~CK & D & RN & ~SE) | (~CK & ~D & RN & SE & SI))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & RN & SE & ~SI) | (~CK & ~D & RN & SE & ~SI) | (~CK & ~D & RN & ~SE))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & ~RN & SE & SI) | (~CK & D & ~RN & ~SE) | (~CK & ~D & ~RN & SE & SI))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & ~SE))
			(negedge SN => (QN-:1'b1)) = 0;
		ifnone (negedge SN => (QN-:1'b1)) = 0;
		if ((CK & ~RN))
			(posedge SN => (QN-:1'b0)) = 0;
		if ((~CK & D & ~RN & SE & SI) | (~CK & D & ~RN & ~SE) | (~CK & ~D & ~RN & SE & SI))
			(posedge SN => (QN-:1'b0)) = 0;
		if ((~CK & D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & SE & ~SI) | (~CK & ~D & ~RN & ~SE))
			(posedge SN => (QN-:1'b0)) = 0;
		ifnone (posedge SN => (QN-:1'b0)) = 0;
		$setuphold (posedge CK &&& adacond_RN_AND_NOT_SE_AND_SN, posedge D &&& adacond_RN_AND_NOT_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_RN_AND_NOT_SE_AND_SN, negedge D &&& adacond_RN_AND_NOT_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_D_AND_RN_AND_NOT_SI_AND_SN, posedge SE &&& adacond_D_AND_RN_AND_NOT_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_D_AND_RN_AND_NOT_SI_AND_SN, negedge SE &&& adacond_D_AND_RN_AND_NOT_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_RN_AND_SI_AND_SN, posedge SE &&& adacond_NOT_D_AND_RN_AND_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_RN_AND_SI_AND_SN, negedge SE &&& adacond_NOT_D_AND_RN_AND_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_RN_AND_SE_AND_SN, posedge SI &&& adacond_RN_AND_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$setuphold (posedge CK &&& adacond_RN_AND_SE_AND_SN, negedge SI &&& adacond_RN_AND_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$setuphold (posedge RN &&& adacond_CK, posedge SN &&& adacond_CK, 0, 0, notifier,,, delayed_RN, delayed_SN);
		$setuphold (posedge RN &&& adacond_NOT_CK, posedge SN &&& adacond_NOT_CK, 0, 0, notifier,,, delayed_RN, delayed_SN);
		$recovery (posedge RN &&& adacond_SE_AND_SI_AND_SN, posedge CK &&& adacond_SE_AND_SI_AND_SN, 0, notifier);
		$recovery (posedge RN &&& adacond_D_AND_NOT_SE_AND_SN, posedge CK &&& adacond_D_AND_NOT_SE_AND_SN, 0, notifier);
		$hold (posedge CK &&& adacond_SE_AND_SI_AND_SN, posedge RN &&& adacond_SE_AND_SI_AND_SN, 0, notifier);
		$hold (posedge CK &&& adacond_D_AND_NOT_SE_AND_SN, posedge RN &&& adacond_D_AND_NOT_SE_AND_SN, 0, notifier);
		$recovery (posedge SN &&& adacond_RN_AND_SE_AND_NOT_SI, posedge CK &&& adacond_RN_AND_SE_AND_NOT_SI, 0, notifier);
		$recovery (posedge SN &&& adacond_NOT_D_AND_RN_AND_NOT_SE, posedge CK &&& adacond_NOT_D_AND_RN_AND_NOT_SE, 0, notifier);
		$hold (posedge CK &&& adacond_RN_AND_SE_AND_NOT_SI, posedge SN &&& adacond_RN_AND_SE_AND_NOT_SI, 0, notifier);
		$hold (posedge CK &&& adacond_NOT_D_AND_RN_AND_NOT_SE, posedge SN &&& adacond_NOT_D_AND_RN_AND_NOT_SE, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge RN, 0, 0, notifier);
		$width (negedge SN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: SDFFS 
`timescale 1ns/10ps
`celldefine
module SDFFSX1 (Q, QN, CK, D, SE, SI, SN);
	output Q, QN;
	input CK, D, SE, SI, SN;
	reg notifier;
	wire delayed_CK, delayed_D, delayed_SE, delayed_SI;

	// Function
	wire delayed_SE__bar, int_fwire_0, int_fwire_1;
	wire int_fwire_2, int_fwire_d, int_fwire_IQ;
	wire int_fwire_IQN, int_fwire_s, xcr_0;

	and (int_fwire_0, delayed_SE, delayed_SI);
	and (int_fwire_1, delayed_D, delayed_SI);
	not (delayed_SE__bar, delayed_SE);
	and (int_fwire_2, delayed_D, delayed_SE__bar);
	or (int_fwire_d, int_fwire_2, int_fwire_1, int_fwire_0);
	not (int_fwire_s, SN);
	altos_dff_s_err (xcr_0, delayed_CK, int_fwire_d, int_fwire_s);
	altos_dff_s (int_fwire_IQ, notifier, delayed_CK, int_fwire_d, int_fwire_s, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_D_AND_NOT_SI_AND_SN, adacond_NOT_D_AND_NOT_SE, adacond_NOT_D_AND_SI_AND_SN;
	wire adacond_NOT_SE_AND_SN, adacond_SE_AND_NOT_SI, adacond_SE_AND_SN;
	wire D__bar, SE__bar, SI__bar;


	// Additional timing gates
	not (SE__bar, SE);
	and (adacond_NOT_SE_AND_SN, SE__bar, SN);
	not (SI__bar, SI);
	and (adacond_D_AND_NOT_SI_AND_SN, D, SI__bar, SN);
	not (D__bar, D);
	and (adacond_NOT_D_AND_SI_AND_SN, D__bar, SI, SN);
	and (adacond_SE_AND_SN, SE, SN);
	and (adacond_SE_AND_NOT_SI, SE, SI__bar);
	and (adacond_NOT_D_AND_NOT_SE, D__bar, SE__bar);

	specify
		if ((SE & SN))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if ((~SE & SN))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if (CK)
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & SE & SI) | (~CK & D & ~SE) | (~CK & ~D & SE & SI))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & SE & ~SI) | (~CK & ~D & SE & ~SI) | (~CK & ~D & ~SE))
			(negedge SN => (Q+:1'b1)) = 0;
		ifnone (negedge SN => (Q+:1'b1)) = 0;
		if ((SE & SN))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if ((~SE & SN))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if (CK)
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & SE & SI) | (~CK & D & ~SE) | (~CK & ~D & SE & SI))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & SE & ~SI) | (~CK & ~D & SE & ~SI) | (~CK & ~D & ~SE))
			(negedge SN => (QN-:1'b1)) = 0;
		ifnone (negedge SN => (QN-:1'b1)) = 0;
		$setuphold (posedge CK &&& adacond_NOT_SE_AND_SN, posedge D &&& adacond_NOT_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_NOT_SE_AND_SN, negedge D &&& adacond_NOT_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_D_AND_NOT_SI_AND_SN, posedge SE &&& adacond_D_AND_NOT_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_D_AND_NOT_SI_AND_SN, negedge SE &&& adacond_D_AND_NOT_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_SI_AND_SN, posedge SE &&& adacond_NOT_D_AND_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_SI_AND_SN, negedge SE &&& adacond_NOT_D_AND_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_SE_AND_SN, posedge SI &&& adacond_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$setuphold (posedge CK &&& adacond_SE_AND_SN, negedge SI &&& adacond_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$recovery (posedge SN &&& adacond_SE_AND_NOT_SI, posedge CK &&& adacond_SE_AND_NOT_SI, 0, notifier);
		$recovery (posedge SN &&& adacond_NOT_D_AND_NOT_SE, posedge CK &&& adacond_NOT_D_AND_NOT_SE, 0, notifier);
		$hold (posedge CK &&& adacond_SE_AND_NOT_SI, posedge SN &&& adacond_SE_AND_NOT_SI, 0, notifier);
		$hold (posedge CK &&& adacond_NOT_D_AND_NOT_SE, posedge SN &&& adacond_NOT_D_AND_NOT_SE, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge SN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: SDFFS 
`timescale 1ns/10ps
`celldefine
module SDFFSX4 (Q, QN, CK, D, SE, SI, SN);
	output Q, QN;
	input CK, D, SE, SI, SN;
	reg notifier;
	wire delayed_CK, delayed_D, delayed_SE, delayed_SI;

	// Function
	wire delayed_SE__bar, int_fwire_0, int_fwire_1;
	wire int_fwire_2, int_fwire_d, int_fwire_IQ;
	wire int_fwire_IQN, int_fwire_s, xcr_0;

	and (int_fwire_0, delayed_SE, delayed_SI);
	and (int_fwire_1, delayed_D, delayed_SI);
	not (delayed_SE__bar, delayed_SE);
	and (int_fwire_2, delayed_D, delayed_SE__bar);
	or (int_fwire_d, int_fwire_2, int_fwire_1, int_fwire_0);
	not (int_fwire_s, SN);
	altos_dff_s_err (xcr_0, delayed_CK, int_fwire_d, int_fwire_s);
	altos_dff_s (int_fwire_IQ, notifier, delayed_CK, int_fwire_d, int_fwire_s, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_D_AND_NOT_SI_AND_SN, adacond_NOT_D_AND_NOT_SE, adacond_NOT_D_AND_SI_AND_SN;
	wire adacond_NOT_SE_AND_SN, adacond_SE_AND_NOT_SI, adacond_SE_AND_SN;
	wire D__bar, SE__bar, SI__bar;


	// Additional timing gates
	not (SE__bar, SE);
	and (adacond_NOT_SE_AND_SN, SE__bar, SN);
	not (SI__bar, SI);
	and (adacond_D_AND_NOT_SI_AND_SN, D, SI__bar, SN);
	not (D__bar, D);
	and (adacond_NOT_D_AND_SI_AND_SN, D__bar, SI, SN);
	and (adacond_SE_AND_SN, SE, SN);
	and (adacond_SE_AND_NOT_SI, SE, SI__bar);
	and (adacond_NOT_D_AND_NOT_SE, D__bar, SE__bar);

	specify
		if ((SE & SN))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if ((~SE & SN))
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if (CK)
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & SE & SI) | (~CK & D & ~SE) | (~CK & ~D & SE & SI))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~CK & D & SE & ~SI) | (~CK & ~D & SE & ~SI) | (~CK & ~D & ~SE))
			(negedge SN => (Q+:1'b1)) = 0;
		ifnone (negedge SN => (Q+:1'b1)) = 0;
		if ((SE & SN))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if ((~SE & SN))
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if (CK)
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & SE & SI) | (~CK & D & ~SE) | (~CK & ~D & SE & SI))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~CK & D & SE & ~SI) | (~CK & ~D & SE & ~SI) | (~CK & ~D & ~SE))
			(negedge SN => (QN-:1'b1)) = 0;
		ifnone (negedge SN => (QN-:1'b1)) = 0;
		$setuphold (posedge CK &&& adacond_NOT_SE_AND_SN, posedge D &&& adacond_NOT_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_NOT_SE_AND_SN, negedge D &&& adacond_NOT_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_D_AND_NOT_SI_AND_SN, posedge SE &&& adacond_D_AND_NOT_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_D_AND_NOT_SI_AND_SN, negedge SE &&& adacond_D_AND_NOT_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_SI_AND_SN, posedge SE &&& adacond_NOT_D_AND_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_SI_AND_SN, negedge SE &&& adacond_NOT_D_AND_SI_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_SE_AND_SN, posedge SI &&& adacond_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$setuphold (posedge CK &&& adacond_SE_AND_SN, negedge SI &&& adacond_SE_AND_SN, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$recovery (posedge SN &&& adacond_SE_AND_NOT_SI, posedge CK &&& adacond_SE_AND_NOT_SI, 0, notifier);
		$recovery (posedge SN &&& adacond_NOT_D_AND_NOT_SE, posedge CK &&& adacond_NOT_D_AND_NOT_SE, 0, notifier);
		$hold (posedge CK &&& adacond_SE_AND_NOT_SI, posedge SN &&& adacond_SE_AND_NOT_SI, 0, notifier);
		$hold (posedge CK &&& adacond_NOT_D_AND_NOT_SE, posedge SN &&& adacond_NOT_D_AND_NOT_SE, 0, notifier);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
		$width (negedge SN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: SDFF 
`timescale 1ns/10ps
`celldefine
module SDFFX1 (Q, QN, CK, D, SE, SI);
	output Q, QN;
	input CK, D, SE, SI;
	reg notifier;
	wire delayed_CK, delayed_D, delayed_SE, delayed_SI;

	// Function
	wire delayed_SE__bar, int_fwire_0, int_fwire_1;
	wire int_fwire_2, int_fwire_d, int_fwire_IQ;
	wire int_fwire_IQN, xcr_0;

	and (int_fwire_0, delayed_SE, delayed_SI);
	and (int_fwire_1, delayed_D, delayed_SI);
	not (delayed_SE__bar, delayed_SE);
	and (int_fwire_2, delayed_D, delayed_SE__bar);
	or (int_fwire_d, int_fwire_2, int_fwire_1, int_fwire_0);
	altos_dff_err (xcr_0, delayed_CK, int_fwire_d);
	altos_dff (int_fwire_IQ, notifier, delayed_CK, int_fwire_d, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_D_AND_NOT_SI, adacond_NOT_D_AND_SI, adacond_NOT_SE;
	wire adacond_SE, D__bar, SI__bar;


	// Additional timing gates
	not (adacond_NOT_SE, SE);
	not (SI__bar, SI);
	and (adacond_D_AND_NOT_SI, D, SI__bar);
	not (D__bar, D);
	and (adacond_NOT_D_AND_SI, D__bar, SI);
	buf (adacond_SE, SE);

	specify
		if (SE)
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if (~SE)
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if (SE)
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if (~SE)
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		$setuphold (posedge CK &&& adacond_NOT_SE, posedge D &&& adacond_NOT_SE, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_NOT_SE, negedge D &&& adacond_NOT_SE, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_D_AND_NOT_SI, posedge SE &&& adacond_D_AND_NOT_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_D_AND_NOT_SI, negedge SE &&& adacond_D_AND_NOT_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_SI, posedge SE &&& adacond_NOT_D_AND_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_SI, negedge SE &&& adacond_NOT_D_AND_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_SE, posedge SI &&& adacond_SE, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$setuphold (posedge CK &&& adacond_SE, negedge SI &&& adacond_SE, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: SDFF 
`timescale 1ns/10ps
`celldefine
module SDFFX4 (Q, QN, CK, D, SE, SI);
	output Q, QN;
	input CK, D, SE, SI;
	reg notifier;
	wire delayed_CK, delayed_D, delayed_SE, delayed_SI;

	// Function
	wire delayed_SE__bar, int_fwire_0, int_fwire_1;
	wire int_fwire_2, int_fwire_d, int_fwire_IQ;
	wire int_fwire_IQN, xcr_0;

	and (int_fwire_0, delayed_SE, delayed_SI);
	and (int_fwire_1, delayed_D, delayed_SI);
	not (delayed_SE__bar, delayed_SE);
	and (int_fwire_2, delayed_D, delayed_SE__bar);
	or (int_fwire_d, int_fwire_2, int_fwire_1, int_fwire_0);
	altos_dff_err (xcr_0, delayed_CK, int_fwire_d);
	altos_dff (int_fwire_IQ, notifier, delayed_CK, int_fwire_d, xcr_0);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_D_AND_NOT_SI, adacond_NOT_D_AND_SI, adacond_NOT_SE;
	wire adacond_SE, D__bar, SI__bar;


	// Additional timing gates
	not (adacond_NOT_SE, SE);
	not (SI__bar, SI);
	and (adacond_D_AND_NOT_SI, D, SI__bar);
	not (D__bar, D);
	and (adacond_NOT_D_AND_SI, D__bar, SI);
	buf (adacond_SE, SE);

	specify
		if (SE)
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if (~SE)
			(posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (Q+:((SE && SI) || (!SE && D)))) = 0;
		if (SE)
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		if (~SE)
			(posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		ifnone (posedge CK => (QN-:((SE && SI) || (!SE && D)))) = 0;
		$setuphold (posedge CK &&& adacond_NOT_SE, posedge D &&& adacond_NOT_SE, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_NOT_SE, negedge D &&& adacond_NOT_SE, 0, 0, notifier,,, delayed_CK, delayed_D);
		$setuphold (posedge CK &&& adacond_D_AND_NOT_SI, posedge SE &&& adacond_D_AND_NOT_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_D_AND_NOT_SI, negedge SE &&& adacond_D_AND_NOT_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_SI, posedge SE &&& adacond_NOT_D_AND_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_NOT_D_AND_SI, negedge SE &&& adacond_NOT_D_AND_SI, 0, 0, notifier,,, delayed_CK, delayed_SE);
		$setuphold (posedge CK &&& adacond_SE, posedge SI &&& adacond_SE, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$setuphold (posedge CK &&& adacond_SE, negedge SI &&& adacond_SE, 0, 0, notifier,,, delayed_CK, delayed_SI);
		$width (posedge CK, 0, 0, notifier);
		$width (negedge CK, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: TBUF 
`timescale 1ns/10ps
`celldefine
module TBUFX1 (Y, A, OE);
	output Y;
	input A, OE;

	// Function
	bufif1 (Y, A, OE);

	// Timing
	specify
		(A => Y) = 0;
		(OE => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: TBUF 
`timescale 1ns/10ps
`celldefine
module TBUFX4 (Y, A, OE);
	output Y;
	input A, OE;

	// Function
	bufif1 (Y, A, OE);

	// Timing
	specify
		(A => Y) = 0;
		(OE => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: TBUF 
`timescale 1ns/10ps
`celldefine
module TBUFX8 (Y, A, OE);
	output Y;
	input A, OE;

	// Function
	bufif1 (Y, A, OE);

	// Timing
	specify
		(A => Y) = 0;
		(OE => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type:  
`timescale 1ns/10ps
`celldefine
module TIEHI (Y);
	output Y;

	// Function
	buf (Y, 1'b1);

	// Timing
	specify
	endspecify
endmodule
`endcelldefine

// type:  
`timescale 1ns/10ps
`celldefine
module TIELO (Y);
	output Y;

	// Function
	buf (Y, 1'b0);

	// Timing
	specify
	endspecify
endmodule
`endcelldefine

// type: LAT 
`timescale 1ns/10ps
`celldefine
module TLATSRX1 (Q, QN, D, G, RN, SN);
	output Q, QN;
	input D, G, RN, SN;
	reg notifier;
	wire delayed_D, delayed_G, delayed_RN, delayed_SN;

	// Function
	wire int_fwire_IQ, int_fwire_IQN, int_fwire_r;
	wire int_fwire_s;

	not (int_fwire_s, delayed_SN);
	not (int_fwire_r, delayed_RN);
	altos_latch_sr_1 (int_fwire_IQ, notifier, delayed_G, delayed_D, int_fwire_s, int_fwire_r);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing

	// Additional timing wires
	wire adacond_D_AND_SN, adacond_NOT_D_AND_RN, adacond_NOT_G;
	wire adacond_RN_AND_SN, D__bar;


	// Additional timing gates
	and (adacond_RN_AND_SN, RN, SN);
	and (adacond_D_AND_SN, D, SN);
	not (adacond_NOT_G, G);
	not (D__bar, D);
	and (adacond_NOT_D_AND_RN, D__bar, RN);

	specify
		(D => Q) = 0;
		(posedge G => (Q+:D)) = 0;
		if ((D & G & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		if ((~G & SN))
			(negedge RN => (Q+:1'b0)) = 0;
		ifnone (negedge RN => (Q+:1'b0)) = 0;
		ifnone (posedge RN => (Q+:1'b1)) = 0;
		if ((~D & G & RN))
			(posedge SN => (Q+:1'b0)) = 0;
		if (~RN)
			(posedge SN => (Q+:1'b0)) = 0;
		ifnone (posedge SN => (Q+:1'b0)) = 0;
		if ((~G & RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if ((~D & G & RN))
			(negedge SN => (Q+:1'b1)) = 0;
		if (~RN)
			(negedge SN => (Q+:1'b1)) = 0;
		ifnone (negedge SN => (Q+:1'b1)) = 0;
		(D => QN) = 0;
		(posedge G => (QN-:D)) = 0;
		(posedge RN => (QN-:1'b1)) = 0;
		if ((D & G & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		if ((~G & SN))
			(negedge RN => (QN-:1'b0)) = 0;
		ifnone (negedge RN => (QN-:1'b0)) = 0;
		if ((~G & RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if ((~D & G & RN))
			(negedge SN => (QN-:1'b1)) = 0;
		if (~RN)
			(negedge SN => (QN-:1'b1)) = 0;
		ifnone (negedge SN => (QN-:1'b1)) = 0;
		if ((~D & G & RN))
			(posedge SN => (QN-:1'b0)) = 0;
		if (~RN)
			(posedge SN => (QN-:1'b0)) = 0;
		ifnone (posedge SN => (QN-:1'b0)) = 0;
		$setuphold (negedge G &&& adacond_RN_AND_SN, posedge D &&& adacond_RN_AND_SN, 0, 0, notifier,,, delayed_G, delayed_D);
		$setuphold (negedge G &&& adacond_RN_AND_SN, negedge D &&& adacond_RN_AND_SN, 0, 0, notifier,,, delayed_G, delayed_D);
		$setuphold (posedge SN &&& adacond_NOT_G, posedge RN &&& adacond_NOT_G, 0, 0, notifier,,, delayed_SN, delayed_RN);
		$setuphold (posedge RN &&& adacond_NOT_G, posedge SN &&& adacond_NOT_G, 0, 0, notifier,,, delayed_RN, delayed_SN);
		$recovery (posedge RN &&& adacond_D_AND_SN, negedge G &&& adacond_D_AND_SN, 0, notifier);
		$hold (negedge G &&& adacond_D_AND_SN, posedge RN &&& adacond_D_AND_SN, 0, notifier);
		$recovery (posedge SN &&& adacond_NOT_D_AND_RN, negedge G &&& adacond_NOT_D_AND_RN, 0, notifier);
		$hold (negedge G &&& adacond_NOT_D_AND_RN, posedge SN &&& adacond_NOT_D_AND_RN, 0, notifier);
		$width (posedge G, 0, 0, notifier);
		$width (negedge RN, 0, 0, notifier);
		$width (negedge SN, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: LAT 
`timescale 1ns/10ps
`celldefine
module TLATX1 (Q, QN, D, G);
	output Q, QN;
	input D, G;
	reg notifier;
	wire delayed_D, delayed_G;

	// Function
	wire int_fwire_IQ, int_fwire_IQN;

	altos_latch (int_fwire_IQ, notifier, delayed_G, delayed_D);
	buf (Q, int_fwire_IQ);
	not (int_fwire_IQN, int_fwire_IQ);
	buf (QN, int_fwire_IQN);

	// Timing
	specify
		(D => Q) = 0;
		(posedge G => (Q+:D)) = 0;
		(D => QN) = 0;
		(posedge G => (QN-:D)) = 0;
		$setuphold (negedge G, posedge D, 0, 0, notifier,,, delayed_G, delayed_D);
		$setuphold (negedge G, negedge D, 0, 0, notifier,,, delayed_G, delayed_D);
		$width (posedge G, 0, 0, notifier);
	endspecify
endmodule
`endcelldefine

// type: XNOR2 
`timescale 1ns/10ps
`celldefine
module XNOR2X1 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar, int_fwire_0;
	wire int_fwire_1;

	not (B__bar, B);
	not (A__bar, A);
	and (int_fwire_0, A__bar, B__bar);
	and (int_fwire_1, A, B);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if (B)
			(A => Y) = 0;
		ifnone (A => Y) = 0;
		if (~B)
			(A => Y) = 0;
		if (A)
			(B => Y) = 0;
		ifnone (B => Y) = 0;
		if (~A)
			(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: XNOR2 
`timescale 1ns/10ps
`celldefine
module XNOR2X2 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar, int_fwire_0;
	wire int_fwire_1;

	not (B__bar, B);
	not (A__bar, A);
	and (int_fwire_0, A__bar, B__bar);
	and (int_fwire_1, A, B);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if (B)
			(A => Y) = 0;
		ifnone (A => Y) = 0;
		if (~B)
			(A => Y) = 0;
		if (A)
			(B => Y) = 0;
		ifnone (B => Y) = 0;
		if (~A)
			(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: XNOR2 
`timescale 1ns/10ps
`celldefine
module XNOR2X4 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar, int_fwire_0;
	wire int_fwire_1;

	not (B__bar, B);
	not (A__bar, A);
	and (int_fwire_0, A__bar, B__bar);
	and (int_fwire_1, A, B);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if (B)
			(A => Y) = 0;
		ifnone (A => Y) = 0;
		if (~B)
			(A => Y) = 0;
		if (A)
			(B => Y) = 0;
		ifnone (B => Y) = 0;
		if (~A)
			(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: XOR2 
`timescale 1ns/10ps
`celldefine
module XOR2X1 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar, int_fwire_0;
	wire int_fwire_1;

	not (A__bar, A);
	and (int_fwire_0, A__bar, B);
	not (B__bar, B);
	and (int_fwire_1, A, B__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if (~B)
			(A => Y) = 0;
		ifnone (A => Y) = 0;
		if (B)
			(A => Y) = 0;
		if (~A)
			(B => Y) = 0;
		ifnone (B => Y) = 0;
		if (A)
			(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: XOR2 
`timescale 1ns/10ps
`celldefine
module XOR2X2 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar, int_fwire_0;
	wire int_fwire_1;

	not (A__bar, A);
	and (int_fwire_0, A__bar, B);
	not (B__bar, B);
	and (int_fwire_1, A, B__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if (~B)
			(A => Y) = 0;
		ifnone (A => Y) = 0;
		if (B)
			(A => Y) = 0;
		if (~A)
			(B => Y) = 0;
		ifnone (B => Y) = 0;
		if (A)
			(B => Y) = 0;
	endspecify
endmodule
`endcelldefine

// type: XOR2 
`timescale 1ns/10ps
`celldefine
module XOR2X4 (Y, A, B);
	output Y;
	input A, B;

	// Function
	wire A__bar, B__bar, int_fwire_0;
	wire int_fwire_1;

	not (A__bar, A);
	and (int_fwire_0, A__bar, B);
	not (B__bar, B);
	and (int_fwire_1, A, B__bar);
	or (Y, int_fwire_1, int_fwire_0);

	// Timing
	specify
		if (~B)
			(A => Y) = 0;
		ifnone (A => Y) = 0;
		if (B)
			(A => Y) = 0;
		if (~A)
			(B => Y) = 0;
		ifnone (B => Y) = 0;
		if (A)
			(B => Y) = 0;
	endspecify
endmodule
`endcelldefine


`ifdef _udp_def_altos_latch_
`else
`define _udp_def_altos_latch_
primitive altos_latch (q, v, clk, d);
	output q;
	reg q;
	input v, clk, d;

	table
		* ? ? : ? : x;
		? 1 0 : ? : 0;
		? 1 1 : ? : 1;
		? x 0 : 0 : -;
		? x 1 : 1 : -;
		? 0 ? : ? : -;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_dff_err_
`else
`define _udp_def_altos_dff_err_
primitive altos_dff_err (q, clk, d);
	output q;
	reg q;
	input clk, d;

	table
		(0x) ? : ? : 0;
		(1x) ? : ? : 1;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_dff_
`else
`define _udp_def_altos_dff_
primitive altos_dff (q, v, clk, d, xcr);
	output q;
	reg q;
	input v, clk, d, xcr;

	table
		*  ?   ? ? : ? : x;
		? (x1) 0 0 : ? : 0;
		? (x1) 1 0 : ? : 1;
		? (x1) 0 1 : 0 : 0;
		? (x1) 1 1 : 1 : 1;
		? (x1) ? x : ? : -;
		? (bx) 0 ? : 0 : -;
		? (bx) 1 ? : 1 : -;
		? (x0) b ? : ? : -;
		? (x0) ? x : ? : -;
		? (01) 0 ? : ? : 0;
		? (01) 1 ? : ? : 1;
		? (10) ? ? : ? : -;
		?  b   * ? : ? : -;
		?  ?   ? * : ? : -;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_dff_r_err_
`else
`define _udp_def_altos_dff_r_err_
primitive altos_dff_r_err (q, clk, d, r);
	output q;
	reg q;
	input clk, d, r;

	table
		 ?   0 (0x) : ? : -;
		 ?   0 (x0) : ? : -;
		(0x) ?  0   : ? : 0;
		(0x) 0  x   : ? : 0;
		(1x) ?  0   : ? : 1;
		(1x) 0  x   : ? : 1;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_dff_r_
`else
`define _udp_def_altos_dff_r_
primitive altos_dff_r (q, v, clk, d, r, xcr);
	output q;
	reg q;
	input v, clk, d, r, xcr;

	table
		*  ?   ?  ?   ? : ? : x;
		?  ?   ?  1   ? : ? : 0;
		?  b   ? (1?) ? : 0 : -;
		?  x   0 (1?) ? : 0 : -;
		?  ?   ? (10) ? : ? : -;
		?  ?   ? (x0) ? : ? : -;
		?  ?   ? (0x) ? : 0 : -;
		? (x1) 0  ?   0 : ? : 0;
		? (x1) 1  0   0 : ? : 1;
		? (x1) 0  ?   1 : 0 : 0;
		? (x1) 1  0   1 : 1 : 1;
		? (x1) ?  ?   x : ? : -;
		? (bx) 0  ?   ? : 0 : -;
		? (bx) 1  0   ? : 1 : -;
		? (x0) 0  ?   ? : ? : -;
		? (x0) 1  0   ? : ? : -;
		? (x0) ?  0   x : ? : -;
		? (01) 0  ?   ? : ? : 0;
		? (01) 1  0   ? : ? : 1;
		? (10) ?  ?   ? : ? : -;
		?  b   *  ?   ? : ? : -;
		?  ?   ?  ?   * : ? : -;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_dff_s_err_
`else
`define _udp_def_altos_dff_s_err_
primitive altos_dff_s_err (q, clk, d, s);
	output q;
	reg q;
	input clk, d, s;

	table
		 ?   1 (0x) : ? : -;
		 ?   1 (x0) : ? : -;
		(0x) ?  0   : ? : 0;
		(0x) 1  x   : ? : 0;
		(1x) ?  0   : ? : 1;
		(1x) 1  x   : ? : 1;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_dff_s_
`else
`define _udp_def_altos_dff_s_
primitive altos_dff_s (q, v, clk, d, s, xcr);
	output q;
	reg q;
	input v, clk, d, s, xcr;

	table
		*  ?   ?  ?   ? : ? : x;
		?  ?   ?  1   ? : ? : 1;
		?  b   ? (1?) ? : 1 : -;
		?  x   1 (1?) ? : 1 : -;
		?  ?   ? (10) ? : ? : -;
		?  ?   ? (x0) ? : ? : -;
		?  ?   ? (0x) ? : 1 : -;
		? (x1) 0  0   0 : ? : 0;
		? (x1) 1  ?   0 : ? : 1;
		? (x1) 1  ?   1 : 1 : 1;
		? (x1) 0  0   1 : 0 : 0;
		? (x1) ?  ?   x : ? : -;
		? (bx) 1  ?   ? : 1 : -;
		? (bx) 0  0   ? : 0 : -;
		? (x0) 1  ?   ? : ? : -;
		? (x0) 0  0   ? : ? : -;
		? (x0) ?  0   x : ? : -;
		? (01) 1  ?   ? : ? : 1;
		? (01) 0  0   ? : ? : 0;
		? (10) ?  ?   ? : ? : -;
		?  b   *  ?   ? : ? : -;
		?  ?   ?  ?   * : ? : -;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_dff_sr_err_
`else
`define _udp_def_altos_dff_sr_err_
primitive altos_dff_sr_err (q, clk, d, s, r);
	output q;
	reg q;
	input clk, d, s, r;

	table
		 ?   1 (0x)  ?   : ? : -;
		 ?   0  ?   (0x) : ? : -;
		 ?   0  ?   (x0) : ? : -;
		(0x) ?  0    0   : ? : 0;
		(0x) 1  x    0   : ? : 0;
		(0x) 0  0    x   : ? : 0;
		(1x) ?  0    0   : ? : 1;
		(1x) 1  x    0   : ? : 1;
		(1x) 0  0    x   : ? : 1;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_dff_sr_0
`else
`define _udp_def_altos_dff_sr_0
primitive altos_dff_sr_0 (q, v, clk, d, s, r, xcr);
	output q;
	reg q;
	input v, clk, d, s, r, xcr;

	table
	//	v,  clk, d, s, r : q' : q;

		*  ?   ?   ?   ?   ? : ? : x;
		?  ?   ?   ?   1   ? : ? : 0;
		?  ?   ?   1   0   ? : ? : 1;
		?  b   ? (1?)  0   ? : 1 : -;
		?  x   1 (1?)  0   ? : 1 : -;
		?  ?   ? (10)  0   ? : ? : -;
		?  ?   ? (x0)  0   ? : ? : -;
		?  ?   ? (0x)  0   ? : 1 : -;
		?  b   ?  0   (1?) ? : 0 : -;
		?  x   0  0   (1?) ? : 0 : -;
		?  ?   ?  0   (10) ? : ? : -;
		?  ?   ?  0   (x0) ? : ? : -;
		?  ?   ?  0   (0x) ? : 0 : -;
		? (x1) 0  0    ?   0 : ? : 0;
		? (x1) 1  ?    0   0 : ? : 1;
		? (x1) 0  0    ?   1 : 0 : 0;
		? (x1) 1  ?    0   1 : 1 : 1;
		? (x1) ?  ?    0   x : ? : -;
		? (x1) ?  0    ?   x : ? : -;
		? (1x) 0  0    ?   ? : 0 : -;
		? (1x) 1  ?    0   ? : 1 : -;
		? (x0) 0  0    ?   ? : ? : -;
		? (x0) 1  ?    0   ? : ? : -;
		? (x0) ?  0    0   x : ? : -;
		? (0x) 0  0    ?   ? : 0 : -;
		? (0x) 1  ?    0   ? : 1 : -;
		? (01) 0  0    ?   ? : ? : 0;
		? (01) 1  ?    0   ? : ? : 1;
		? (10) ?  0    ?   ? : ? : -;
		? (10) ?  ?    0   ? : ? : -;
		?  b   *  0    ?   ? : ? : -;
		?  b   *  ?    0   ? : ? : -;
		?  ?   ?  ?    ?   * : ? : -;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_dff_sr_1
`else
`define _udp_def_altos_dff_sr_1
primitive altos_dff_sr_1 (q, v, clk, d, s, r, xcr);
	output q;
	reg q;
	input v, clk, d, s, r, xcr;

	table
	//	v,  clk, d, s, r : q' : q;

		*  ?   ?   ?   ?   ? : ? : x;
		?  ?   ?   0   1   ? : ? : 0;
		?  ?   ?   1   ?   ? : ? : 1;
		?  b   ? (1?)  0   ? : 1 : -;
		?  x   1 (1?)  0   ? : 1 : -;
		?  ?   ? (10)  0   ? : ? : -;
		?  ?   ? (x0)  0   ? : ? : -;
		?  ?   ? (0x)  0   ? : 1 : -;
		?  b   ?  0   (1?) ? : 0 : -;
		?  x   0  0   (1?) ? : 0 : -;
		?  ?   ?  0   (10) ? : ? : -;
		?  ?   ?  0   (x0) ? : ? : -;
		?  ?   ?  0   (0x) ? : 0 : -;
		? (x1) 0  0    ?   0 : ? : 0;
		? (x1) 1  ?    0   0 : ? : 1;
		? (x1) 0  0    ?   1 : 0 : 0;
		? (x1) 1  ?    0   1 : 1 : 1;
		? (x1) ?  ?    0   x : ? : -;
		? (x1) ?  0    ?   x : ? : -;
		? (1x) 0  0    ?   ? : 0 : -;
		? (1x) 1  ?    0   ? : 1 : -;
		? (x0) 0  0    ?   ? : ? : -;
		? (x0) 1  ?    0   ? : ? : -;
		? (x0) ?  0    0   x : ? : -;
		? (0x) 0  0    ?   ? : 0 : -;
		? (0x) 1  ?    0   ? : 1 : -;
		? (01) 0  0    ?   ? : ? : 0;
		? (01) 1  ?    0   ? : ? : 1;
		? (10) ?  0    ?   ? : ? : -;
		? (10) ?  ?    0   ? : ? : -;
		?  b   *  0    ?   ? : ? : -;
		?  b   *  ?    0   ? : ? : -;
		?  ?   ?  ?    ?   * : ? : -;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_latch_r_
`else
`define _udp_def_altos_latch_r_
primitive altos_latch_r (q, v, clk, d, r);
	output q;
	reg q;
	input v, clk, d, r;

	table
		* ? ? ? : ? : x;
		? ? ? 1 : ? : 0;
		? 0 ? 0 : ? : -;
		? 0 ? x : 0 : -;
		? 1 0 0 : ? : 0;
		? 1 0 x : ? : 0;
		? 1 1 0 : ? : 1;
		? x 0 0 : 0 : -;
		? x 0 x : 0 : -;
		? x 1 0 : 1 : -;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_latch_s_
`else
`define _udp_def_altos_latch_s_
primitive altos_latch_s (q, v, clk, d, s);
	output q;
	reg q;
	input v, clk, d, s;

	table
		* ? ? ? : ? : x;
		? ? ? 1 : ? : 1;
		? 0 ? 0 : ? : -;
		? 0 ? x : 1 : -;
		? 1 1 0 : ? : 1;
		? 1 1 x : ? : 1;
		? 1 0 0 : ? : 0;
		? x 1 0 : 1 : -;
		? x 1 x : 1 : -;
		? x 0 0 : 0 : -;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_latch_sr_0
`else
`define _udp_def_altos_latch_sr_0
primitive altos_latch_sr_0 (q, v, clk, d, s, r);
	output q;
	reg q;
	input v, clk, d, s, r;

	table
		* ? ? ? ? : ? : x;
		? 1 1 ? 0 : ? : 1;
		? 1 0 0 ? : ? : 0;
		? ? ? 1 0 : ? : 1;
		? ? ? ? 1 : ? : 0;
		? 0 * ? ? : ? : -;
		? 0 ? * 0 : 1 : 1;
		? 0 ? 0 * : 0 : 0;
		? * 1 ? 0 : 1 : 1;
		? * 0 0 ? : 0 : 0;
		? ? 1 * 0 : 1 : 1;
		? ? 0 0 * : 0 : 0;
	endtable
endprimitive
`endif

`ifdef _udp_def_altos_latch_sr_1
`else
`define _udp_def_altos_latch_sr_1
primitive altos_latch_sr_1 (q, v, clk, d, s, r);
	output q;
	reg q;
	input v, clk, d, s, r;

	table
		* ? ? ? ? : ? : x;
		? 1 1 ? 0 : ? : 1;
		? 1 0 0 ? : ? : 0;
		? ? ? 1 ? : ? : 1;
		? ? ? 0 1 : ? : 0;
		? 0 * ? ? : ? : -;
		? 0 ? * 0 : 1 : 1;
		? 0 ? 0 * : 0 : 0;
		? * 1 ? 0 : 1 : 1;
		? * 0 0 ? : 0 : 0;
		? ? 1 * 0 : 1 : 1;
		? ? 0 0 * : 0 : 0;
	endtable
endprimitive
`endif
