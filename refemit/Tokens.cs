﻿/*
  Copyright (C) 2008 Jeroen Frijters

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

namespace IKVM.Reflection.Emit
{
	public struct ConstructorToken
	{
		internal readonly int Token;

		internal ConstructorToken(int token)
		{
			this.Token = token;
		}

		internal bool IsPseudoToken
		{
			get { return Token < 0; }
		}
	}

	public struct FieldToken
	{
		internal readonly int Token;

		internal FieldToken(int token)
		{
			this.Token = token;
		}

		internal bool IsPseudoToken
		{
			get { return Token < 0; }
		}
	}

	public struct MethodToken
	{
		internal readonly int Token;

		internal MethodToken(int token)
		{
			this.Token = token;
		}

		internal bool IsPseudoToken
		{
			get { return Token < 0; }
		}
	}

	public struct TypeToken
	{
		internal readonly int Token;

		internal TypeToken(int token)
		{
			this.Token = token;
		}
	}
}
