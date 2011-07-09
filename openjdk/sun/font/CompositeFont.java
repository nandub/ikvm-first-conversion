/*
  Copyright (C) 2009, 2011 Volker Berlin (i-net software)

  This software is provided 'as-is', without any express or implied
  warranty.  In no event will the authors be held liable for any damages
  arising from the use of this software.

  Permission is granted to anyone to use this software for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:

  1. The origin of this software must not be misrepresented; you must not
     claim that you wrote the original software. If you use this software
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original software.
  3. This notice may not be removed or altered from any source distribution.

  Jeroen Frijters
  jeroen@frijters.net
  
 */
package sun.font;

import cli.System.Drawing.Font;
import ikvm.internal.NotYetImplementedError;


/**
 * 
 */
public class CompositeFont extends Font2D{

    public CompositeFont(PhysicalFont physicalFont, CompositeFont dialog2d) {
    	throw new NotYetImplementedError();
	}

	@Override
    public int getStyle(){
        throw new NotYetImplementedError();
    }

    @Override
    public Font createNetFont(java.awt.Font font){
        throw new NotYetImplementedError();
    }

    public int getNumSlots() {
        throw new NotYetImplementedError();
    }
    
    public PhysicalFont getSlotFont(int slot) {
        throw new NotYetImplementedError();
    }
}
