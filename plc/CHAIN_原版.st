//AUTO_Chain_Control1(EA_Chain := EA_Chain);
AUTO_Chain_Control1(MODE := MODE, tESTOP := E_ESTOP,EA_Chain := EA_Chain);
(*=========================Initialize===============================*)
IF MODE.Initial_Active AND MODE.Initial_Started THEN
    CASE  EA_Chain.GRDS.Step OF
        0:
            IF tESTOP THEN
                //PB := FALSE;
                stControlAxis[1].xStartTask := FALSE;
                stControlAxis[2].xStartTask := FALSE;
                stControlAxis[3].xStartTask := FALSE;
                EA_Chain.GRDS.Step := EA_Chain.GRDS.Step + 1;
            ELSE
                // 报警
            END_IF
        
        1:
            IF Axis_Ready THEN   
                Axis_Reset := FALSE;         
                EA_Chain.GRDS.Step := EA_Chain.GRDS.Step + 1;  
            ELSE
                Axis_Reset := TRUE;
                Message := Tips(0, "等待伺服复位");
            END_IF    

                
            
        2:  // 3轴回原点          
            SERVO_Move_AbsY(E_TargetPOS := ServoPos[2][0], stStatus := stStateAxis[2], AxisCtrl := stControlAxis[2]);               
                                                                
            IF SERVO_INPOS(ServoPos[2][0], 1, stStateAxis[2]) AND SERVO_Move_AbsY.Done 
            THEN
                stControlAxis[2].xStartTask := FALSE;
                EA_Chain.GRDS.Step := EA_Chain.GRDS.Step + 1;
            END_IF                         
            
            Message := Tips(0, "等待伺服Y回原位");
                    
        
        3:
            //IF stStateAxis[1].diActualPosition < 3400 THEN
            //    Message := Tips(0, "X轴位置有碰撞风险\n请手动移动X轴至安全位");
            //ELSE
            //    EA_Chain.GRDS.Step := EA_Chain.GRDS.Step + 1;  
            //END_IF
            EA_Chain.GRDS.Step := EA_Chain.GRDS.Step + 1;  
        
        
        4:
            SERVO_Move_AbsX(E_TargetPOS := ServoPos[1][10], stStatus := stStateAxis[1], AxisCtrl := stControlAxis[1]);                    
                                 
            IF SERVO_INPOS(ServoPos[1][10], 1, stStateAxis[1]) AND SERVO_Move_AbsX.Done
            THEN
                stControlAxis[1].xStartTask := FALSE;
                EA_Chain.GRDS.Step := EA_Chain.GRDS.Step + 1;
            END_IF                         
            
            Message := Tips(0, "等待伺服X回原位");

        5:              
            SERVO_Move_AbsZ(E_TargetPOS := ServoPos[3][2], stStatus := stStateAxis[3], AxisCtrl := stControlAxis[3]);                     
                                 
            IF SERVO_INPOS(ServoPos[3][2], 1, stStateAxis[3]) AND SERVO_Move_AbsZ.Done
            THEN
                stControlAxis[3].xStartTask := FALSE;
                EA_Chain.GRDS.Step := EA_Chain.GRDS.Step + 1;
            END_IF                         
            
            Message := Tips(0, "等待伺服Z回原位");
        
        
        
        
        6:  // 磁铁失电
            A_Magnet := FALSE;
            EA_Chain.GRDS.Step := EA_Chain.GRDS.Step + 1;
            
        7:  // 相机通讯状态检查 及 ready信号
            IF Camera.CommOK AND Camera.Ready THEN
                EA_Chain.GRDS.Step := EA_Chain.GRDS.Step + 1;
            END_IF
            EA_Chain.GRDS.Step := EA_Chain.GRDS.Step + 1;
            Message := Tips(0, "等待相机就绪");
        
        8:  // 仓库物料状态检查
            IF NOT FULL THEN
                EA_Chain.GRDS.Step := EA_Chain.GRDS.Step + 1;
            ELSE
                Message := Tips(0, "仓库物料已满\n请手动取走并清空");
                // 报警
            END_IF
        
        
        9:
            IF E_Start = FALSE THEN
                EA_Chain.GRDS.Step := 100;
            ELSE
                Message := Tips(0, "启动按钮故障，请检查");
                // 报警
            END_IF
        
        
        
        
        

        
        100:
            EA_Chain.GRDS.Reached := TRUE;
            EA_Chain.Busy := FALSE;
            A_gLED := FALSE;
            Message := Tips(0, "初始化结束"); 
     
       
        ELSE 
            (* Statements *)
    END_CASE
END_IF

IF NOT (EA_Chain.GRDS.Reached AND MODE.Auto_Active AND MODE.AutomaticStarted)
THEN
    RETURN;
END_IF;

mDelay(IN:= EA_Chain.Step=14 OR EA_Chain.Step=24,PT:=T#0.5S);
Photo_Delay(IN:=DLAY, PT:=T#0.8S);

T1(IN:=EA_Chain.Step=7, PT:=T#5S);
// 步超时
TimeOut(IN:= EA_Chain.Step = tempStep AND EA_Chain.Busy,PT:=T#1M);
tempStep := EA_Chain.Step;

tSTEP := EA_Chain.Step;
(*===========================Automatic==============================*)
CASE EA_Chain.Step OF
    
    0:
        IF E_Start THEN
            A_gLED := TRUE;
            EA_Chain.Busy := TRUE;
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF
    
        Message := Tips(0, "请按拍照启动按钮");
    
    
    1:  // 信号复位
        Camera.Start := FALSE;
        Rsc_xExecute := FALSE;
        stControlAxis[1].xStartTask := FALSE;
        stControlAxis[2].xStartTask := FALSE;
        stControlAxis[3].xStartTask := FALSE;
        DLAY := FALSE;
        Pos := 0;
        iType := 0;
        //image_result.index := 0;
        A_Magnet := FALSE;
        EA_Chain.Step := EA_Chain.Step + 1;
    
        
    2:
        IF E_Detection THEN
            Pos := 0;
            iType := 0;
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF
        
        Message := Tips(0, "请放模型");
         
        
        
    3:  // 开始拍照 正常下一步，错误 报警或重拍
        //Camera.Start := TRUE;
        Rsc_xExecute := TRUE;
        DLAY := TRUE;
        IF NOT Camera.Done THEN
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF
        EA_Chain.Step := 6;
        
        Message := Tips(0, "拍照信号未复位");
        
    6:
        //IF Camera.Done THEN
        //    Camera.Start := FALSE;
        //    EA_Chain.Step := EA_Chain.Step + 1;
        //END_IF
        //IF NOT Rsc_xExecute THEN        // 拍照完成
        //    EA_Chain.Step := EA_Chain.Step + 1;
        //END_IF
        IF Photo_Delay.Q THEN           // 延时
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF
    
    7:
        IF Camera.Data > 0 THEN 
            IF Camera.Data <> 100 THEN
                iType := Camera.Data;
                //Pos := Camera.Data;
                EA_Chain.Step := EA_Chain.Step + 1;
            ELSE
                // 报警 或其他处理
            END_IF
        END_IF
        
        IF T1.Q THEN    // 无法识别
            EA_Chain.Step := 1;
        END_IF
        
        Message := Tips(0, "等待拍照结果");
        
    8:  // 拍照结果处理  与 仓库空位比对  
        // 拍照结果分1-2-3-4   1放1竖列3个位置  依此类推
        CASE iType OF
            3:
                IF NOT (MatState[1] = 1) THEN
                    Pos := 1;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSIF NOT (MatState[2] = 2) THEN
                    Pos := 2;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSIF NOT (MatState[3] = 3) THEN
                    Pos := 3;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSE
                    EA_Chain.Step := 40;
                    //Message := Tips(0, "识别错误,无法继续\n请清空重新运行");
                END_IF
            2:
                IF NOT (MatState[4] = 4) THEN
                    Pos := 4;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSIF NOT (MatState[5] = 5) THEN
                    Pos := 5;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSIF NOT (MatState[6] = 6) THEN
                    Pos := 6;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSE
                    EA_Chain.Step := 40;
                    //Message := Tips(0, "识别错误,无法继续\n请清空重新运行");
                END_IF
            4:
                IF NOT (MatState[7] = 7) THEN
                    Pos := 7;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSIF NOT (MatState[8] = 8) THEN
                    Pos := 8;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSIF NOT (MatState[9] = 9) THEN
                    Pos := 9;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSE
                    EA_Chain.Step := 40;
                    //Message := Tips(0, "识别错误,无法继续\n请清空重新运行");
                END_IF
            1:
                IF NOT (MatState[10] = 10) THEN
                    Pos := 10;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSIF NOT (MatState[11] = 11) THEN
                    Pos := 11;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSIF NOT (MatState[12] = 12) THEN
                    Pos := 12;
                    EA_Chain.Step := EA_Chain.Step + 1;
                ELSE
                    EA_Chain.Step := 40;
                    //Message := Tips(0, "识别错误,无法继续\n请清空重新运行");
                END_IF
            ELSE
                EA_Chain.Step := 40;
                //Message := Tips(0, "识别错误,无法继续\n请清空重新运行");
        END_CASE
        
        //EA_Chain.Step := EA_Chain.Step + 1;
   
    
    9:  // 检测伺服状态
        IF Axis_Ready THEN   
            Axis_Reset := FALSE;         
            EA_Chain.Step := EA_Chain.Step + 1;  
        ELSE
            Axis_Reset := TRUE;
            Message := Tips(0, "等待伺服复位");
        END_IF  
    
    
    10: // 伺服去取件位  0为取件位
        SERVO_Move_AbsY(E_TargetPOS := ServoPos[2][0], stStatus := stStateAxis[2], AxisCtrl := stControlAxis[2]);                                   
                             
        IF SERVO_INPOS(ServoPos[2][0], 1, stStateAxis[2]) AND SERVO_Move_AbsY.Done 
        THEN
            
            EA_Chain.Step := EA_Chain.Step + 1;
            stControlAxis[2].xStartTask := FALSE;
        END_IF                         
        
        Message := Tips(0, "等待Y轴去取料位置");
    
    
    
    11:
        SERVO_Move_AbsZ(E_TargetPOS := ServoPos[3][0]+2000, stStatus := stStateAxis[3], AxisCtrl := stControlAxis[3]); 
        IF SERVO_INPOS(ServoPos[3][0]+2000, 1, stStateAxis[3]) AND SERVO_Move_AbsZ.Done THEN
            EA_Chain.Step := EA_Chain.Step + 1;   
            stControlAxis[3].xStartTask := FALSE; 
        END_IF
        Message := Tips(0, "等待Z轴去取料等待位置");
    
    
    12:
        SERVO_Move_AbsX(E_TargetPOS := ServoPos[1][0], stStatus := stStateAxis[1], AxisCtrl := stControlAxis[1]);                    
                             
        IF SERVO_INPOS(ServoPos[1][0], 1, stStateAxis[1]) AND SERVO_Move_AbsX.Done
        THEN
            stControlAxis[1].xStartTask := FALSE;
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF                         
        
        Message := Tips(0, "等待X轴去取料位置");
    
    13:
        SERVO_Move_AbsZ(E_TargetPOS := ServoPos[3][0], stStatus := stStateAxis[3], AxisCtrl := stControlAxis[3]); 
        IF SERVO_INPOS(ServoPos[3][0], 1, stStateAxis[3]) AND SERVO_Move_AbsZ.Done THEN
            EA_Chain.Step := EA_Chain.Step + 1;   
            stControlAxis[3].xStartTask := FALSE; 
        END_IF
        Message := Tips(0, "等待Z轴去取料位置");
     
    
    
    14: // 吸取模型 等待1s
        A_Magnet := TRUE;
        IF mDelay.Q THEN
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF
    
    15: // 取完Z轴抬高
        SERVO_Move_AbsZ(E_TargetPOS := ServoPos[3][0] + 4000, stStatus := stStateAxis[3], AxisCtrl := stControlAxis[3]); 
        IF SERVO_INPOS(ServoPos[3][0] + 4000, 1, stStateAxis[3]) AND SERVO_Move_AbsZ.Done THEN
            stControlAxis[3].xStartTask := FALSE;
            EA_Chain.Step := EA_Chain.Step + 1;    
        END_IF
        Message := Tips(0, "等待Z轴抬高");
    
    16: // 检测是否已经取走
        IF NOT E_Detection THEN
            EA_Chain.Step := 20;
        ELSE
            Message := Tips(0, "模型未取走");
        END_IF
    
        
    20: // 去仓库放料  先X再Z后Y
        SERVO_Move_AbsX(E_TargetPOS := ServoPos[1][Pos], stStatus := stStateAxis[1], AxisCtrl := stControlAxis[1]);                    
                             
        IF SERVO_INPOS(ServoPos[1][Pos], 1, stStateAxis[1]) AND SERVO_Move_AbsX.Done 
        THEN
            stControlAxis[1].xStartTask := FALSE;
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF                         
        
        Message := Tips(0, "等待X轴到位");    
            
    
    21:              
        SERVO_Move_AbsZ(E_TargetPOS := ServoPos[3][Pos]+1000, stStatus := stStateAxis[3], AxisCtrl := stControlAxis[3]);                     
                             
        IF SERVO_INPOS(ServoPos[3][Pos]+1000, 1, stStateAxis[3]) AND SERVO_Move_AbsZ.Done
        THEN
            stControlAxis[3].xStartTask := FALSE;
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF                         
        
        Message := Tips(0, "等待Z轴到等待位"); 
    
    
    22:
        SERVO_Move_AbsY(E_TargetPOS := ServoPos[2][Pos], stStatus := stStateAxis[2], AxisCtrl := stControlAxis[2]);                                   
                             
        IF SERVO_INPOS(ServoPos[2][Pos], 1, stStateAxis[2]) AND SERVO_Move_AbsY.Done
        THEN
            stControlAxis[2].xStartTask := FALSE;
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF                         
        
        Message := Tips(0, "等待Y轴到位"); 
    
    
    23:
        SERVO_Move_AbsZ(E_TargetPOS := ServoPos[3][Pos], stStatus := stStateAxis[3], AxisCtrl := stControlAxis[3]);                     
                             
        IF SERVO_INPOS(ServoPos[3][Pos], 1, stStateAxis[3]) AND SERVO_Move_AbsZ.Done
        THEN
            stControlAxis[3].xStartTask := FALSE;
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF                         
        
        Message := Tips(0, "等待Z轴到位");
           
    
    24:
        A_Magnet := FALSE;
        MatState[Pos] := Pos;   // 放完赋值
        IF mDelay.Q THEN
            EA_Chain.Step := EA_Chain.Step + 1;
        END_IF
    
    25: // 伺服回原位 先Z抬高
        SERVO_Move_AbsZ(E_TargetPOS := ServoPos[3][Pos] + 1000, stStatus := stStateAxis[3], AxisCtrl := stControlAxis[3]); 
        
        IF SERVO_INPOS(ServoPos[3][Pos] + 1000, 1, stStateAxis[3]) AND SERVO_Move_AbsZ.Done THEN
            stControlAxis[3].xStartTask := FALSE;
            EA_Chain.Step := EA_Chain.Step + 1;    
        END_IF
        Message := Tips(0, "等待Z轴抬高");
    
    
    26:
        SERVO_Move_AbsY(E_TargetPOS := ServoPos[2][0], stStatus := stStateAxis[2], AxisCtrl := stControlAxis[2]);                                   
                             
        IF SERVO_INPOS(ServoPos[2][0], 1, stStateAxis[2]) AND SERVO_Move_AbsY.Done
        THEN
            stControlAxis[2].xStartTask := FALSE;
            EA_Chain.Step := 100;
           // EA_Chain.Step := EA_Chain.Step + 1;
        END_IF                         
        
        Message := Tips(0, "等待Y轴回原位"); 
    
    
    //27:
      //  SERVO_Move_AbsX(E_TargetPOS := ServoPos[1][4], stStatus := stStateAxis[1], AxisCtrl := stControlAxis[1]);                    
                             
        //IF SERVO_INPOS(ServoPos[1][4], 1, stStateAxis[1]) AND SERVO_Move_AbsX.Done 
      //  THEN
         //   stControlAxis[1].xStartTask := FALSE;
          //  EA_Chain.Step := 100;
       // END_IF                         
        
       // Message := Tips(0, "等待X轴回原位"); 


    40:
        A_gLED := T2.Q;
        Message := Tips(0, "识别错误,无法继续\n请清空重新运行");
        IF E_Reset THEN
            EA_Chain.Step := 1;
        END_IF
     

    100:
        EA_Chain.Busy := FALSE;
        EA_Chain.Step := 1;



END_CASE

IF E_Detection THEN
    Rsc_xExecute := TRUE;
ELSE
    Rsc_xExecute := FALSE;
END_IF

