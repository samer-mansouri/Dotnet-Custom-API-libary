﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ArchiLogApi.Data;
using ArchiLogApi.Models;
using ApiClassLibrary.Controllers;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchiLogApi.Controllers
{
    /// <summary>
    /// Controlleur pour les voitures, le CRUD est implémenté dans la class mère BaseController
    /// </summary>
    [ApiVersion("1.0")]
    public class CarsController : BaseController<ArchiLogDbContext, Car>
    {
        public CarsController(ArchiLogDbContext context):base(context)
        {
        }
    }
}
