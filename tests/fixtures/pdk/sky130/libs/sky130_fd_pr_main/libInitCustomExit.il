;;
;; Get the Library Path
;;
  libPath = ddGetObjReadPath( ddGetObj( "sky130_fd_pr_main" ) )

;;; Sets the specified environment variable if it hasn't been already set 
;;; differently from its default value
procedure( envSetValIfDefault( tool var type value "ttsg" )
    when( and( envIsVal( tool var ) envGetVal( tool var ) == envGetDefVal( tool var ) )
	envSetVal( tool var type value )
    )
)


printf("\tLoading Environment Settings ...\n")
;;
;; Set the Simulator Model Paths
;;
  if(isContextLoaded("schView") then
    envSetVal("asimenv.startup" "simulator" 'string "spectre")
      asiSetEnvOptionVal(asiGetTool('spectre) "modelFiles"
         list(
            list(strcat( libPath "/../../models/sky130.lib.spice") "tt")
      ))
)

envSetValIfDefault("layoutXL" "setPPConn" 'boolean nil)
envSetValIfDefault("layout" "xSnapSpacing" 'float 0.005)
envSetValIfDefault("layout" "ySnapSpacing" 'float 0.005)
envSetValIfDefault("layoutXL" "initIOPinLayer" 'string "met1 drawing")
envSetValIfDefault("spectre.envOpts" "useAltergroup" 'boolean nil)
;;;envSetValIfDefault("spectre.opts" "scale" 'string  "1.0u")
envSetValIfDefault("hspiceD.envOpts" "autoDisplay" 'boolean nil)
envSetValIfDefault("layoutXL" "lxGenerateInBoundary" 'boolean t)
envSetValIfDefault("layoutXL" "lxPositionMinSep" 'float 0.12)
envSetValIfDefault("layout" "extractVerifyWellSubstrateConnections" 'boolean t)
envSetValIfDefault("layoutXL" "lxEvalCDFCallbacks" 'boolean t)
envSetValIfDefault("layout" "extractStopLevel" 'int 2)
