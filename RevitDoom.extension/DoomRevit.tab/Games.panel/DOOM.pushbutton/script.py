# -*- coding: utf-8 -*-
"""Play DOOM inside Revit.

Click: play the best IWAD found in the extension's wads folder
       (DOOM.WAD / DOOM2.WAD / DOOM1.WAD ... falls back to bundled Freedoom).
Shift+Click: pick any IWAD file.
"""
__title__ = 'DOOM'
__author__ = 'Marcel Žnidarič'
__context__ = 'zero-doc'

import os
from pyrevit import forms, script

import revitdoom

cfg = script.get_config()

try:
    pick = __shiftclick__  # noqa: F821  (injected by pyRevit)
except NameError:
    pick = False

iwads = revitdoom.find_iwads()
last = cfg.get_option('last_wad', '')
wad = None
if not pick:
    if last and os.path.isfile(last):
        wad = last
    elif iwads:
        wad = iwads[0]
if not wad:
    wad = forms.pick_file(file_ext='wad',
                          init_dir=revitdoom.WAD_DIR,
                          title='Pick an IWAD (DOOM.WAD, DOOM2.WAD, DOOM1.WAD, freedoom1.wad)')
    if not wad:
        script.exit()
    cfg.last_wad = wad
    script.save_config()

try:
    doc_title = __revit__.ActiveUIDocument.Document.Title  # noqa: F821
except Exception:
    doc_title = ''

try:
    revitdoom.play(wad, doc_title)
except Exception as ex:
    # Errors inside the engine are shown in the game window's console;
    # this only fires if the window itself could not be created.
    import traceback
    forms.alert('Could not open the DOOM window.\n\n%s\n\n%s' % (ex, traceback.format_exc()),
                title='RevitDoom')
