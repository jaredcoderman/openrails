"""
Curve Fitter Main Entry Point
==============================

Orchestrates the complete pipeline:
1. Extract primitives from railroad GeoJSON data
2. Export to JSON
3. Build and run C# TdbDump project

Run this script to process railroad data into Open Rails primitives.
"""

import subprocess
import sys
from extract_primitives import extract_primitives


def build_and_run_tdbdump(export_data):
    """
    Export primitives to C# project and build/run TdbDump.
    
    Args:
        export_data: dict with segments from extract_primitives()
        
    Returns:
        bool: True if successful, False if error
    """
    try:
        import json
        
        openrails_path = r'C:\Users\jared\main\openrails\Source\TdbDump\primitives.json'
        with open(openrails_path, 'w') as f:
            json.dump(export_data, f, indent=2)
        print(f"\nExported to C# project: {openrails_path}")
        
        print("\n" + "=" * 80)
        print("STEP 6: Building C# project")
        print("=" * 80)
        
        # Build and run TdbDump
        result = subprocess.run(
            r'cd /d C:\Users\jared\main\openrails\Source\TdbDump && dotnet build -c Debug && .\bin\Debug\TdbDump.exe',
            shell=True,
            check=True
        )
        
        print("\n" + "=" * 80)
        print("C# PROJECT BUILD AND EXECUTION COMPLETE")
        print("=" * 80)
        return True
        
    except subprocess.CalledProcessError as e:
        print(f"\nError: C# project build/run failed with exit code {e.returncode}")
        return False
    except FileNotFoundError as e:
        print(f"\nError: Could not write to C# project path: {e}")
        return False
    except Exception as e:
        print(f"\nError: {e}")
        return False


def main():
    """
    Main entry point: extract primitives and build C# project.
    """
    print("=" * 80)
    print("CURVE FITTER - RAILROAD PRIMITIVE EXTRACTION")
    print("=" * 80)
    
    # Step 1: Extract primitives from GeoJSON
    print("\nPHASE 1: EXTRACTING PRIMITIVES FROM GEOJSON")
    print("-" * 80)
    try:
        export_data = extract_primitives()
    except Exception as e:
        print(f"\nFATAL ERROR: Could not extract primitives: {e}")
        sys.exit(1)
    
    # Step 2: Build and run C# TdbDump
    print("\nPHASE 2: BUILDING C# PROJECT")
    print("-" * 80)
    success = build_and_run_tdbdump(export_data)
    
    # Summary
    print("\n" + "=" * 80)
    if success:
        print("SUCCESS: Primitives extracted and C# project executed")
        print("=" * 80)
        sys.exit(0)
    else:
        print("WARNING: Primitives extracted but C# project build/run had issues")
        print("=" * 80)
        sys.exit(1)


if __name__ == '__main__':
    main()
